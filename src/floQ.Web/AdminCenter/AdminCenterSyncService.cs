using System.Net;
using System.Text.Json;
using floQ.Domain.Platform;
using floQ.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace floQ.Web.AdminCenter;

/// <summary>
/// BackgroundService: pullt periodisch den Plattform-Zustand vom
/// batOSAdminCenter — <b>je Tenant einmal</b>, denn jeder Tenant ist im AC
/// eine eigene Instanz (ShortName = Tenant-Slug). Je Tenant und Durchgang:
/// 1. GET /api/global-settings → Shutoff-Zustand des Tenants übernehmen
///    (Felder am Tenant + <see cref="TenantShutoffService"/>); globale
///    Assets nur beim ersten Tenant des Durchgangs (ETag/If-None-Match,
///    <see cref="PlatformAsset"/>).
/// 2. Katalog-Push (POST …/module-catalog), einmal je Tenant und
///    Prozess-Lauf — der Katalog ist statisch je Deploy.
/// 3. GET …/subscriptions → Abo-Cache des Tenants spiegeln
///    (<see cref="EnabledModule"/>: aktive Keys upserten, nicht mehr
///    gemeldete löschen), danach Gating neu laden.
///
/// Bewusste Designentscheidungen (identisch zum batOS-Core-Pattern):
/// - Push/Pull-Prinzip: Pull ist die einzige Wahrheitsquelle. Die AC-Pushes
///   (sync/shutoff) tragen keine Instanz-Identität — alle Tenants teilen
///   denselben Host — und wecken deshalb nur den Loop
///   (<see cref="IAdminCenterSyncTrigger"/>). Intervall = reiner Fallback.
/// - Auto-Discovery: der erste Pull mit einem neuen Slug legt die Instanz
///   im AC an. Neue Registrierungen stoßen den Loop sofort an
///   (PasskeyService → RequestSync) und erscheinen damit unmittelbar im AC.
/// - Fehlertolerant: jeder Schritt und jeder Tenant fängt seine Exceptions
///   selbst — ein AC-Ausfall kostet nie Konsistenz, die lokalen Caches
///   bleiben gültig.
/// </summary>
public class AdminCenterSyncService(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpFactory,
    IOptions<AdminCenterOptions> options,
    IAdminCenterSyncTrigger trigger,
    TenantShutoffService tenantShutoff,
    ModuleCatalog catalog,
    ModuleGateService moduleGate,
    ILogger<AdminCenterSyncService> log) : BackgroundService
{
    private readonly AdminCenterOptions _options = options.Value;
    private readonly HashSet<Guid> _catalogPushed = [];

    public const string HttpClientName = "AdminCenter";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_options.IsConfigured)
        {
            log.LogWarning("AdminCenter nicht konfiguriert (PlatformKey leer) — Sync-Service bleibt untätig.");
            return;
        }

        // Anlaufverzögerung: DB-Migration + App-Warmup abwarten.
        try { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
        catch (OperationCanceledException) { return; }

        log.LogInformation("AdminCenter-Sync gestartet: {BaseUrl}, eine Instanz je Tenant (Intervall {Interval}).",
            _options.BaseUrl, _options.PullInterval);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PullAllTenantsAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "AdminCenter-Pull fehlgeschlagen — nächster Versuch nach Intervall/Push.");
            }

            try { await trigger.WaitAsync(_options.PullInterval, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private sealed record TenantRef(Guid Id, string Slug, string Name);

    private async Task PullAllTenantsAsync(CancellationToken ct)
    {
        List<TenantRef> tenants;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenants = await db.Tenants
                .Where(t => t.Slug != "")
                .Select(t => new TenantRef(t.Id, t.Slug, t.Name))
                .ToListAsync(ct);
        }

        var assetsProcessed = false;
        foreach (var tenant in tenants)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await PullTenantAsync(tenant, processAssets: !assetsProcessed, ct);
                assetsProcessed = true;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "AdminCenter-Pull für Tenant '{Slug}' fehlgeschlagen — Cache bleibt.", tenant.Slug);
            }
        }
    }

    private async Task PullTenantAsync(TenantRef tenant, bool processAssets, CancellationToken ct)
    {
        var http = CreateClient(tenant);

        var resp = await http.GetAsync("/api/global-settings", ct);
        if (!resp.IsSuccessStatusCode)
        {
            log.LogWarning("AdminCenter /api/global-settings ({Slug}) → {Status}", tenant.Slug, (int)resp.StatusCode);
            return;
        }

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("data", out var data)) return;

        await ProcessShutoffAsync(tenant, data, ct);

        // Folgeschritte fangen ihre Fehler selbst — ein kaputter Schritt darf
        // die anderen nicht mitreißen (Shutoff ist bereits verarbeitet).
        if (processAssets)
        {
            try
            {
                await ProcessAssetsAsync(http, data, ct);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "AdminCenter-Sync: Asset-Abgleich fehlgeschlagen — Cache bleibt.");
            }
        }

        try
        {
            await PushCatalogOnceAsync(http, tenant, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "AdminCenter-Sync: Katalog-Push ({Slug}) fehlgeschlagen — Retry beim nächsten Pull.", tenant.Slug);
        }

        try
        {
            await SyncSubscriptionsAsync(http, tenant, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "AdminCenter-Sync: Abo-Sync ({Slug}) fehlgeschlagen — alter Abo-Cache bleibt gültig.", tenant.Slug);
        }
    }

    private async Task ProcessShutoffAsync(TenantRef tenant, JsonElement data, CancellationToken ct)
    {
        if (!data.TryGetProperty("shutoff", out var so)) return;

        var active = so.TryGetProperty("active", out var a) && a.ValueKind == JsonValueKind.True;
        var reason = so.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";
        var at = so.TryGetProperty("at", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Tenants.SingleOrDefaultAsync(x => x.Id == tenant.Id, ct);
        if (row is null) return;

        if (row.ShutoffActive == active && row.ShutoffReason == reason && row.ShutoffAt == at)
            return;

        row.ShutoffActive = active;
        row.ShutoffReason = reason;
        row.ShutoffAt = at;
        await db.SaveChangesAsync(ct);
        tenantShutoff.Reload();

        if (active)
            log.LogWarning("AdminCenter-Shutoff AKTIV für Tenant '{Slug}': '{Reason}'", tenant.Slug, reason);
        else
            log.LogInformation("AdminCenter-Shutoff für Tenant '{Slug}' aufgehoben.", tenant.Slug);
    }

    /// <summary>Gleicht alle in global-settings gelisteten Assets ab: GET mit
    /// If-None-Match, bei 304 unverändert, bei 200 Bytes + ETag in den
    /// <see cref="PlatformAsset"/>-Cache. Assets sind global je Software —
    /// ein Abgleich je Durchgang genügt.</summary>
    private async Task ProcessAssetsAsync(HttpClient http, JsonElement data, CancellationToken ct)
    {
        if (!data.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return;

        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("key", out var k) || k.ValueKind != JsonValueKind.String)
                continue;
            var key = k.GetString();
            if (string.IsNullOrWhiteSpace(key)) continue;

            await ProcessAssetAsync(http, key, ct);
        }
    }

    private async Task ProcessAssetAsync(HttpClient http, string key, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.PlatformAssets.SingleOrDefaultAsync(a => a.Key == key, ct);

        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/assets/{key}");
        if (!string.IsNullOrEmpty(row?.ETag))
            req.Headers.TryAddWithoutValidation("If-None-Match", row.ETag);

        using var resp = await http.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.NotModified) return;
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            log.LogInformation("AdminCenter-Asset '{Key}' existiert nicht (404) — Cache bleibt.", key);
            return;
        }
        if (!resp.IsSuccessStatusCode)
        {
            log.LogWarning("AdminCenter-Asset '{Key}' → {Status}", key, (int)resp.StatusCode);
            return;
        }

        if (row is null)
        {
            row = new PlatformAsset { Key = key };
            db.PlatformAssets.Add(row);
        }
        row.Content = await resp.Content.ReadAsByteArrayAsync(ct);
        row.ContentType = resp.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        row.ETag = resp.Headers.ETag?.Tag ?? "";
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        log.LogInformation("AdminCenter-Asset '{Key}' aktualisiert ({Bytes} Bytes, ETag {ETag}).",
            key, row.Content.Length, row.ETag);
    }

    /// <summary>Meldet dem AC den Modul-Katalog dieser Code-Version (Replace-
    /// Semantik: das AC kennt danach exakt diese Keys). Einmal je Tenant und
    /// Prozess-Lauf — der Katalog ist statisch je Deploy. Non-2xx wird still
    /// behandelt (loggen, weiterlaufen), Retry beim nächsten Pull.</summary>
    private async Task PushCatalogOnceAsync(HttpClient http, TenantRef tenant, CancellationToken ct)
    {
        lock (_catalogPushed)
        {
            if (_catalogPushed.Contains(tenant.Id)) return;
        }

        var payload = new
        {
            modules = catalog.All.Select(m => new
            {
                key = m.Key,
                kind = m.Kind == ModuleKind.Tool ? "tool" : "module",
                title = m.Title,
                subtitle = m.Subtitle,
                description = m.Description,
                icon = m.Icon,
                iconColor = m.IconColor,
                route = m.Route,
                audience = m.Audience,
            }).ToList(),
        };

        using var resp = await http.PostAsJsonAsync(
            $"/api/instances/{tenant.Slug}/module-catalog", payload, ct);

        if (resp.IsSuccessStatusCode)
        {
            lock (_catalogPushed) { _catalogPushed.Add(tenant.Id); }
            log.LogInformation("AdminCenter: Modul-Katalog gepusht ({Slug}, {Count} Einträge).",
                tenant.Slug, payload.modules.Count);
        }
        else
        {
            log.LogWarning("AdminCenter /module-catalog ({Slug}) → {Status}", tenant.Slug, (int)resp.StatusCode);
        }
    }

    /// <summary>Holt die aktive Abo-Liste des Tenants vom AC und spiegelt sie
    /// in den lokalen <see cref="EnabledModule"/>-Cache (batOS-Semantik:
    /// aktive Keys upserten, nicht mehr gemeldete löschen). Danach Gating
    /// neu laden.</summary>
    private async Task SyncSubscriptionsAsync(HttpClient http, TenantRef tenant, CancellationToken ct)
    {
        var resp = await http.GetAsync($"/api/instances/{tenant.Slug}/subscriptions", ct);
        if (!resp.IsSuccessStatusCode)
        {
            log.LogWarning("AdminCenter /subscriptions ({Slug}) → {Status}", tenant.Slug, (int)resp.StatusCode);
            return;
        }

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("modules", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return;

        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in arr.EnumerateArray())
        {
            // Kontrakt: Array aus Key-Strings. Objekt-Einträge ({"key": …})
            // werden toleriert, falls das AC das Format später anreichert.
            var key = item.ValueKind switch
            {
                JsonValueKind.String => item.GetString(),
                JsonValueKind.Object when item.TryGetProperty("key", out var k)
                    && k.ValueKind == JsonValueKind.String => k.GetString(),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(key))
                active.Add(key.Trim());
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var existing = await db.EnabledModules
            .Where(e => e.TenantId == tenant.Id)
            .ToListAsync(ct);

        var added = active
            .Where(key => !existing.Any(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var removed = existing
            .Where(e => !active.Contains(e.Key))
            .ToList();

        foreach (var row in existing.Except(removed))
            row.LastSeenActiveAtUtc = now;
        foreach (var key in added)
            db.EnabledModules.Add(new EnabledModule { TenantId = tenant.Id, Key = key, LastSeenActiveAtUtc = now });
        db.EnabledModules.RemoveRange(removed);

        await db.SaveChangesAsync(ct);
        moduleGate.Reload();

        if (added.Count > 0 || removed.Count > 0)
        {
            log.LogInformation("AdminCenter-Abo-Sync ({Slug}): +{Added} -{Removed} (aktiv: {Keys})",
                tenant.Slug, added.Count, removed.Count, string.Join(", ", active));
        }
    }

    private HttpClient CreateClient(TenantRef tenant)
    {
        var http = httpFactory.CreateClient(HttpClientName);
        http.BaseAddress = new Uri(_options.BaseUrl);
        http.Timeout = TimeSpan.FromSeconds(15);
        http.DefaultRequestHeaders.Add("X-Platform-Key", _options.PlatformKey);
        http.DefaultRequestHeaders.Add("X-Instance-ShortName", tenant.Slug);
        if (!string.IsNullOrWhiteSpace(_options.Host))
            http.DefaultRequestHeaders.Add("X-Instance-Host", _options.Host);
        var displayName = ToHeaderSafe(tenant.Name);
        if (!string.IsNullOrWhiteSpace(displayName))
            http.DefaultRequestHeaders.Add("X-Instance-DisplayName", displayName);
        return http;
    }

    /// <summary>HTTP-Header erlauben kein Nicht-ASCII — Tenant-Namen können
    /// aber Umlaute enthalten. Nicht darstellbare Zeichen werden ersetzt,
    /// statt den Sync des Tenants an einer Header-Exception sterben zu lassen.</summary>
    private static string ToHeaderSafe(string value) =>
        string.Concat(value.Select(c => c < 128 && !char.IsControl(c) ? c : '?')).Trim();
}
