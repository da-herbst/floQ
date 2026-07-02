using System.Text.Json;
using floQ.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace floQ.Web.AdminCenter;

/// <summary>
/// Inbound-Endpoints für das batOSAdminCenter (Minimal-API):
///
/// - POST /api/platform/sync       — datenloser Sync-Anstoß (AC → floQ),
///                                   weckt den Pull-Loop sofort.
/// - POST /api/admincenter/shutoff — Shutoff-Webhook. Trägt keine Instanz-
///                                   Identität (alle Tenants teilen den
///                                   Host floq.at), darum wird er NICHT als
///                                   Zustand übernommen, sondern nur als
///                                   Anstoß behandelt: der sofortige Pull
///                                   holt den Shutoff-Zustand je Tenant aus
///                                   global-settings (Quelle der Wahrheit).
/// - GET  /api/billing-payer       — Rechnungs-Stammdaten des Tenants für
///                                   den AC-Rechnungslauf (Pull-on-Invoice:
///                                   das AC ruft bei jeder Rechnungserstellung
///                                   und friert die Antwort als Snapshot ein).
///                                   Tenant kommt aus X-Instance-ShortName.
/// - GET  /health                  — Liveness für Deploy-Verify.
/// - GET  /__subscription          — Status-Ping (aktiv + Version).
///
/// Auth der AC-Endpoints: X-Platform-Key, constant-time-Vergleich.
/// </summary>
public static class AdminCenterEndpoints
{
    public static void MapAdminCenterEndpoints(this WebApplication app)
    {
        app.MapPost("/api/platform/sync", (
            HttpContext ctx,
            IOptions<AdminCenterOptions> opts,
            IAdminCenterSyncTrigger trigger,
            ILogger<AdminCenterSyncTrigger> log) =>
        {
            if (!ValidateKey(ctx, opts.Value, log)) return Results.Unauthorized();

            trigger.RequestSync();
            log.LogInformation("Platform-Sync-Push empfangen — Pull wird sofort ausgeführt.");
            return Results.Ok(new { success = true, data = (object?)null, errorMessage = (string?)null });
        }).AllowAnonymous();

        app.MapPost("/api/admincenter/shutoff", async (
            HttpContext ctx,
            IOptions<AdminCenterOptions> opts,
            IAdminCenterSyncTrigger trigger,
            ILogger<AdminCenterSyncTrigger> log,
            CancellationToken ct) =>
        {
            if (!ValidateKey(ctx, opts.Value, log)) return Results.Unauthorized();

            // Body nur fürs Log lesen — die Wahrheit holt der Pull je Tenant.
            var active = false;
            var reason = "";
            try
            {
                using var doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ct);
                active = doc.RootElement.TryGetProperty("active", out var a) && a.ValueKind == JsonValueKind.True;
                reason = doc.RootElement.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";
            }
            catch (JsonException)
            {
                // Datenlos/kaputt ist egal — der Anstoß zählt.
            }

            trigger.RequestSync();
            log.LogWarning("AdminCenter-Shutoff-Webhook (active={Active}, reason='{Reason}') — Sofort-Pull ausgelöst.",
                active, reason);
            return Results.Ok(new { success = true, data = (object?)null, errorMessage = (string?)null });
        }).AllowAnonymous();

        // Rechnungs-Stammdaten (Payer) für den AC-Rechnungslauf. Das AC ruft
        // bei JEDER Rechnungserstellung und friert die Antwort als Snapshot
        // ein — floQ liefert immer den aktuellen CompanyProfile-Stand,
        // cached und versioniert nichts.
        app.MapGet("/api/billing-payer", async (
            HttpContext ctx,
            IOptions<AdminCenterOptions> opts,
            AppDbContext db,
            ILogger<AdminCenterOptions> log,
            CancellationToken ct) =>
        {
            if (!ValidateKey(ctx, opts.Value, log)) return Results.Unauthorized();

            var slug = ctx.Request.Headers["X-Instance-ShortName"].ToString().Trim().ToLowerInvariant();
            if (slug.Length == 0) return Results.NotFound();

            var tenant = await db.Tenants.SingleOrDefaultAsync(t => t.Slug == slug, ct);
            if (tenant is null)
            {
                log.LogWarning("billing-payer: unbekannter Slug '{Slug}'.", slug);
                return Results.NotFound();
            }

            // Anonymer Plattform-Call → kein TenantContext, Global Query
            // Filter würde leer matchen. Daher explizit ungefiltert + TenantId.
            var profile = await db.CompanyProfiles
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(c => c.TenantId == tenant.Id, ct);

            if (profile is null || string.IsNullOrWhiteSpace(profile.LegalName))
            {
                // AC nutzt dann den Snapshot der letzten Rechnung bzw.
                // überspringt und probiert stündlich erneut.
                return Results.Conflict(new { error = "company_profile_incomplete" });
            }

            return Results.Ok(new
            {
                name = profile.LegalName,
                address = NullIfEmpty(profile.Street),
                zip = NullIfEmpty(profile.ZipCode),
                city = NullIfEmpty(profile.City),
                country = NullIfEmpty(profile.CountryCode),
                uid = NullIfEmpty(profile.VatId),
                email = NullIfEmpty(profile.Email),
            });
        }).AllowAnonymous();

        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
           .AllowAnonymous();

        app.MapGet("/__subscription", () => Results.Json(new
        {
            active = true,
            product = "floq",
            version = Environment.GetEnvironmentVariable("FLOQ_COMMIT_SHA") ?? "unknown",
        })).AllowAnonymous();
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>Constant-time-Vergleich des X-Platform-Key-Headers.
    /// Internal: auch die Lebenszyklus-Endpoints (AdminCenterTenantEndpoints)
    /// authentifizieren damit.</summary>
    internal static bool ValidateKey(HttpContext ctx, AdminCenterOptions opts, ILogger log)
    {
        var expected = opts.PlatformKey;
        if (string.IsNullOrEmpty(expected))
        {
            log.LogWarning("AC-Endpoint aufgerufen, aber Instanz ist nicht ans AdminCenter angebunden.");
            return false;
        }

        var sent = ctx.Request.Headers["X-Platform-Key"].ToString();
        if (sent.Length != expected.Length) return false;
        var diff = 0;
        for (var i = 0; i < sent.Length; i++) diff |= sent[i] ^ expected[i];
        if (diff != 0)
        {
            log.LogWarning("AC-Push mit ungültigem Platform-Key abgewiesen (IP {Ip}).",
                ctx.Connection.RemoteIpAddress);
            return false;
        }
        return true;
    }
}
