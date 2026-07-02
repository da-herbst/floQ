using System.Net;
using System.Text.Json;
using floQ.Domain.Platform;
using floQ.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace floQ.Web.AdminCenter;

/// <summary>
/// BackgroundService: pullt periodisch den Plattform-Zustand vom
/// batOSAdminCenter und spiegelt ihn in die lokalen Caches. Je Durchgang:
/// 1. GET /api/global-settings → Shutoff-Zustand übernehmen
///    (<see cref="PlatformStateService"/>), geänderte Assets per
///    ETag/If-None-Match nachladen (<see cref="PlatformAsset"/>).
/// 2. Katalog-Push (POST …/module-catalog), einmal je Prozess-Lauf —
///    der Katalog ist statisch je Deploy.
/// 3. GET …/subscriptions → Abo-Cache spiegeln (<see cref="EnabledModule"/>:
///    aktive Keys upserten, nicht mehr gemeldete löschen) und das
///    Modul-Gating neu laden (<see cref="ModuleGateService"/>).
///
/// Bewusste Designentscheidungen (identisch zum batOS-Core-Pattern):
/// - Push/Pull-Prinzip: Pull ist die einzige Wahrheitsquelle. Das AC pusht
///   bei Änderungen nur einen datenlosen Anstoß auf POST /api/platform/sync
///   (<see cref="IAdminCenterSyncTrigger"/>). Intervall = reiner Fallback.
/// - Auto-Discovery: erster Pull legt die Instanz im AC an (Upsert per
///   X-Instance-ShortName). Niemand pflegt Instanzen manuell.
/// - Fehlertolerant: jeder Schritt fängt seine Exceptions selbst — ein
///   AC-Ausfall kostet nie Konsistenz, die lokalen Caches bleiben gültig.
/// </summary>
public class AdminCenterSyncService(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpFactory,
    IOptions<AdminCenterOptions> options,
    IAdminCenterSyncTrigger trigger,
    PlatformStateService platformState,
    ModuleCatalog catalog,
    ModuleGateService moduleGate,
    ILogger<AdminCenterSyncService> log) : BackgroundService
{
    private readonly AdminCenterOptions _options = options.Value;
    private bool _catalogPushed;

    public const string HttpClientName = "AdminCenter";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_options.IsConfigured)
        {
            log.LogWarning("AdminCenter nicht konfiguriert (PlatformKey/ShortName leer) — Sync-Service bleibt untätig.");
            return;
        }

        // Anlaufverzögerung: DB-Migration + App-Warmup abwarten.
        try { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
        catch (OperationCanceledException) { return; }

        log.LogInformation("AdminCenter-Sync gestartet: {BaseUrl} als '{ShortName}' (Intervall {Interval}).",
            _options.BaseUrl, _options.ShortName, _options.PullInterval);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PullOnceAsync(ct);
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

    private async Task PullOnceAsync(CancellationToken ct)
    {
        var http = CreateClient();

        var resp = await http.GetAsync("/api/global-settings", ct);
        if (!resp.IsSuccessStatusCode)
        {
            log.LogWarning("AdminCenter /api/global-settings → {Status}", (int)resp.StatusCode);
            return;
        }

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("data", out var data)) return;

        await ProcessShutoffAsync(data, ct);

        // Folgeschritte fangen ihre Fehler selbst — ein kaputter Schritt darf
        // die anderen nicht mitreißen (Shutoff ist bereits verarbeitet).
        try
        {
            await ProcessAssetsAsync(http, data, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "AdminCenter-Sync: Asset-Abgleich fehlgeschlagen — Cache bleibt.");
        }

        try
        {
            await PushCatalogOnceAsync(http, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "AdminCenter-Sync: Katalog-Push fehlgeschlagen — Retry beim nächsten Pull.");
        }

        try
        {
            await SyncSubscriptionsAsync(http, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "AdminCenter-Sync: Abo-Sync fehlgeschlagen — alter Abo-Cache bleibt gültig.");
        }
    }

    private async Task ProcessShutoffAsync(JsonElement data, CancellationToken ct)
    {
        if (!data.TryGetProperty("shutoff", out var so)) return;

        var active = so.TryGetProperty("active", out var a) && a.ValueKind == JsonValueKind.True;
        var reason = so.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";
        var at = so.TryGetProperty("at", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";

        if (platformState.ShutoffActive == active
            && platformState.ShutoffReason == reason
            && platformState.ShutoffAt == at)
            return;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await platformState.WriteShutoffAsync(db, active, reason, at, ct);

        if (active)
            log.LogWarning("AdminCenter-Shutoff AKTIV: '{Reason}'", reason);
        else
            log.LogInformation("AdminCenter-Shutoff aufgehoben.");
    }

    /// <summary>Gleicht alle in global-settings gelisteten Assets ab: GET mit
    /// If-None-Match, bei 304 unverändert, bei 200 Bytes + ETag in den
    /// <see cref="PlatformAsset"/>-Cache.</summary>
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
    /// Semantik: das AC kennt danach exakt diese Keys). Einmal je Prozess-Lauf
    /// — der Katalog ist statisch je Deploy. Non-2xx wird still behandelt
    /// (loggen, weiterlaufen), Retry beim nächsten Pull.</summary>
    private async Task PushCatalogOnceAsync(HttpClient http, CancellationToken ct)
    {
        if (_catalogPushed) return;

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
            $"/api/instances/{_options.ShortName}/module-catalog", payload, ct);

        if (resp.IsSuccessStatusCode)
        {
            _catalogPushed = true;
            log.LogInformation("AdminCenter: Modul-Katalog gepusht ({Count} Einträge).", payload.modules.Count);
        }
        else
        {
            log.LogWarning("AdminCenter /module-catalog → {Status}", (int)resp.StatusCode);
        }
    }

    /// <summary>Holt die aktive Abo-Liste vom AC und spiegelt sie in den
    /// lokalen <see cref="EnabledModule"/>-Cache (batOS-Semantik: aktive Keys
    /// upserten, nicht mehr gemeldete löschen). Danach Gating neu laden.</summary>
    private async Task SyncSubscriptionsAsync(HttpClient http, CancellationToken ct)
    {
        var resp = await http.GetAsync($"/api/instances/{_options.ShortName}/subscriptions", ct);
        if (!resp.IsSuccessStatusCode)
        {
            log.LogWarning("AdminCenter /subscriptions → {Status}", (int)resp.StatusCode);
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
        var existing = await db.EnabledModules.ToListAsync(ct);

        var added = active
            .Where(key => !existing.Any(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var removed = existing
            .Where(e => !active.Contains(e.Key))
            .ToList();

        foreach (var row in existing.Except(removed))
            row.LastSeenActiveAtUtc = now;
        foreach (var key in added)
            db.EnabledModules.Add(new EnabledModule { Key = key, LastSeenActiveAtUtc = now });
        db.EnabledModules.RemoveRange(removed);

        await db.SaveChangesAsync(ct);
        moduleGate.Reload();

        if (added.Count > 0 || removed.Count > 0)
        {
            log.LogInformation("AdminCenter-Abo-Sync: +{Added} -{Removed} (aktiv: {Keys})",
                added.Count, removed.Count, string.Join(", ", active));
        }
    }

    private HttpClient CreateClient()
    {
        var http = httpFactory.CreateClient(HttpClientName);
        http.BaseAddress = new Uri(_options.BaseUrl);
        http.Timeout = TimeSpan.FromSeconds(15);
        http.DefaultRequestHeaders.Add("X-Platform-Key", _options.PlatformKey);
        http.DefaultRequestHeaders.Add("X-Instance-ShortName", _options.ShortName);
        if (!string.IsNullOrWhiteSpace(_options.Host))
            http.DefaultRequestHeaders.Add("X-Instance-Host", _options.Host);
        if (!string.IsNullOrWhiteSpace(_options.DisplayName))
            http.DefaultRequestHeaders.Add("X-Instance-DisplayName", _options.DisplayName);
        return http;
    }
}
