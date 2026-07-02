using System.Text.Json;
using Microsoft.Extensions.Options;

namespace floQ.Web.AdminCenter;

/// <summary>
/// Typisierter Client für tenant-bezogene Request/Response-Calls ans
/// batOSAdminCenter (Abo-Anfragen; später Billing/Rechnungen, sobald der
/// AC-Kontrakt dafür steht). Auth wie beim Sync: X-Platform-Key +
/// X-Instance-ShortName (= Tenant-Slug).
///
/// Abgrenzung zum <see cref="AdminCenterSyncService"/>: der Sync spiegelt
/// AC-Zustand zyklisch in lokale Caches (Gating darf NIE live callen) —
/// dieser Client ist für explizite User-Aktionen und Anzeigen, bei denen
/// ein Live-Call fachlich gewollt ist (z.B. "Anfrage läuft"-Status).
/// AC nicht erreichbar → null bzw. Error-Ergebnis, nie Exception zum Caller.
/// </summary>
public class AdminCenterClient(
    IHttpClientFactory httpFactory,
    IOptions<AdminCenterOptions> options,
    ILogger<AdminCenterClient> log)
{
    private readonly AdminCenterOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    public sealed record PendingRequest(string Module, Guid RequestId);

    /// <summary>Offene (pending) Abo-Anfragen des Tenants — live vom AC.
    /// null = AC nicht konfiguriert/erreichbar (Anzeige dann ohne Status).</summary>
    public async Task<IReadOnlyList<PendingRequest>?> GetPendingModuleRequestsAsync(
        string tenantSlug, CancellationToken ct)
    {
        if (!IsConfigured) return null;
        try
        {
            var http = CreateClient(tenantSlug);
            var resp = await http.GetAsync($"/api/instances/{tenantSlug}/module-requests", ct);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning("AdminCenter /module-requests ({Slug}) → {Status}", tenantSlug, (int)resp.StatusCode);
                return null;
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("requests", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return [];

            var result = new List<PendingRequest>();
            foreach (var item in arr.EnumerateArray())
            {
                var module = item.TryGetProperty("module", out var m) ? m.GetString() : null;
                if (string.IsNullOrWhiteSpace(module)) continue;
                if (item.TryGetProperty("requestId", out var r) && r.TryGetGuid(out var requestId))
                    result.Add(new PendingRequest(module, requestId));
            }
            return result;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "AdminCenter /module-requests ({Slug}) nicht erreichbar.", tenantSlug);
            return null;
        }
    }

    public enum ModuleRequestOutcome
    {
        /// <summary>Neue Anfrage angelegt (AC: 201).</summary>
        Created,
        /// <summary>Es gab schon eine offene Anfrage — idempotent (AC: 200).</summary>
        AlreadyPending,
        /// <summary>Modul ist bereits aktiv abonniert (AC: 409).</summary>
        AlreadySubscribed,
        /// <summary>Key existiert nicht in der floq-Registry des AC (AC: 404).</summary>
        UnknownModule,
        /// <summary>AC nicht konfiguriert/erreichbar oder unerwartete Antwort.</summary>
        Unavailable,
    }

    public sealed record ModuleRequestResult(ModuleRequestOutcome Outcome, Guid? RequestId);

    /// <summary>Stellt eine Abo-Anfrage für den Tenant (Mandant → Hersteller).</summary>
    public async Task<ModuleRequestResult> RequestModuleAsync(
        string tenantSlug, string moduleKey, CancellationToken ct)
    {
        if (!IsConfigured)
            return new ModuleRequestResult(ModuleRequestOutcome.Unavailable, null);

        try
        {
            var http = CreateClient(tenantSlug);
            using var resp = await http.PostAsJsonAsync(
                $"/api/instances/{tenantSlug}/module-requests", new { module = moduleKey }, ct);

            switch ((int)resp.StatusCode)
            {
                case 201 or 200:
                {
                    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                    Guid? id = doc.RootElement.TryGetProperty("id", out var i) && i.TryGetGuid(out var g) ? g : null;
                    var outcome = (int)resp.StatusCode == 201
                        ? ModuleRequestOutcome.Created
                        : ModuleRequestOutcome.AlreadyPending;
                    return new ModuleRequestResult(outcome, id);
                }
                case 409:
                    return new ModuleRequestResult(ModuleRequestOutcome.AlreadySubscribed, null);
                case 404:
                    return new ModuleRequestResult(ModuleRequestOutcome.UnknownModule, null);
                default:
                    log.LogWarning("AdminCenter POST /module-requests ({Slug}, {Module}) → {Status}",
                        tenantSlug, moduleKey, (int)resp.StatusCode);
                    return new ModuleRequestResult(ModuleRequestOutcome.Unavailable, null);
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "AdminCenter POST /module-requests ({Slug}, {Module}) nicht erreichbar.",
                tenantSlug, moduleKey);
            return new ModuleRequestResult(ModuleRequestOutcome.Unavailable, null);
        }
    }

    /// <summary>Eine 5050-Abo-Rechnung aus dem AC-Archiv (read-only, floQ
    /// zeigt sie nur an — Rechnungssteller ist die 5050 development gmbh).
    /// Datumswerte als ISO-Strings 1:1 vom AC; periodEnd ist EXKLUSIV.
    /// Beträge in Cent.</summary>
    public sealed record AcInvoice(
        Guid Id,
        string Number,
        string Status,
        string? PeriodStart,
        string? PeriodEnd,
        string? IssueDate,
        string? DueDate,
        long NetCents,
        decimal VatRatePercent,
        long VatCents,
        long GrossCents,
        string? Currency,
        string? Note);

    /// <summary>Rechnungsliste des Tenants — live vom AC (Rechnungen
    /// entstehen monatlich, Laden beim Seitenaufruf genügt, kein Sync).
    /// null = AC nicht konfiguriert/erreichbar.</summary>
    public async Task<IReadOnlyList<AcInvoice>?> GetInvoicesAsync(string tenantSlug, CancellationToken ct)
    {
        if (!IsConfigured) return null;
        try
        {
            var http = CreateClient(tenantSlug);
            var resp = await http.GetAsync($"/api/instances/{tenantSlug}/invoices", ct);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning("AdminCenter /invoices ({Slug}) → {Status}", tenantSlug, (int)resp.StatusCode);
                return null;
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("invoices", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return [];

            var result = new List<AcInvoice>();
            foreach (var item in arr.EnumerateArray())
            {
                // Die Id steckt im pdfUrl-Segment (…/invoices/{guid}/pdf) —
                // floQ baut daraus seine eigene Proxy-Route, die AC-URL
                // erreicht den Browser nie.
                var pdfUrl = GetString(item, "pdfUrl") ?? "";
                var segments = pdfUrl.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length < 2 || !Guid.TryParse(segments[^2], out var id))
                    continue;

                result.Add(new AcInvoice(
                    id,
                    GetString(item, "number") ?? "",
                    GetString(item, "status") ?? "",
                    GetString(item, "periodStart"),
                    GetString(item, "periodEnd"),
                    GetString(item, "issueDate"),
                    GetString(item, "dueDate"),
                    GetLong(item, "netCents"),
                    GetDecimal(item, "vatRatePercent"),
                    GetLong(item, "vatCents"),
                    GetLong(item, "grossCents"),
                    GetString(item, "currency"),
                    GetString(item, "note")));
            }
            return result;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "AdminCenter /invoices ({Slug}) nicht erreichbar.", tenantSlug);
            return null;
        }
    }

    public sealed record InvoicePdfResult(int StatusCode, byte[]? Content, string? ETag, string? FileName);

    /// <summary>Rechnungs-PDF vom AC holen (serverseitiger Proxy — der
    /// Platform-Key erreicht nie den Browser). If-None-Match wird
    /// durchgereicht; 304 kommt als StatusCode zurück. null = AC nicht
    /// konfiguriert/erreichbar.</summary>
    public async Task<InvoicePdfResult?> GetInvoicePdfAsync(
        string tenantSlug, Guid invoiceId, string? ifNoneMatch, CancellationToken ct)
    {
        if (!IsConfigured) return null;
        try
        {
            var http = CreateClient(tenantSlug);
            using var req = new HttpRequestMessage(
                HttpMethod.Get, $"/api/instances/{tenantSlug}/invoices/{invoiceId}/pdf");
            if (!string.IsNullOrEmpty(ifNoneMatch))
                req.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);

            using var resp = await http.SendAsync(req, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotModified)
                return new InvoicePdfResult(StatusCodes.Status304NotModified, null, null, null);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new InvoicePdfResult(StatusCodes.Status404NotFound, null, null, null);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning("AdminCenter /invoices/{Id}/pdf ({Slug}) → {Status}",
                    invoiceId, tenantSlug, (int)resp.StatusCode);
                return null;
            }

            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            var etag = resp.Headers.ETag?.Tag;
            var fileName = resp.Content.Headers.ContentDisposition?.FileNameStar
                ?? resp.Content.Headers.ContentDisposition?.FileName?.Trim('"');
            return new InvoicePdfResult(StatusCodes.Status200OK, bytes, etag, fileName);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "AdminCenter /invoices/{Id}/pdf ({Slug}) nicht erreichbar.", invoiceId, tenantSlug);
            return null;
        }
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long GetLong(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

    private static decimal GetDecimal(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : 0m;

    private HttpClient CreateClient(string tenantSlug)
    {
        var http = httpFactory.CreateClient(AdminCenterSyncService.HttpClientName);
        http.BaseAddress = new Uri(_options.BaseUrl);
        http.Timeout = TimeSpan.FromSeconds(10);
        http.DefaultRequestHeaders.Add("X-Platform-Key", _options.PlatformKey);
        http.DefaultRequestHeaders.Add("X-Instance-ShortName", tenantSlug);
        if (!string.IsNullOrWhiteSpace(_options.Host))
            http.DefaultRequestHeaders.Add("X-Instance-Host", _options.Host);
        return http;
    }
}
