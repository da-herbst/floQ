using System.Text.Json;
using floQ.Web.Data;
using Microsoft.Extensions.Options;

namespace floQ.Web.AdminCenter;

/// <summary>
/// BackgroundService: pullt periodisch den Plattform-Zustand vom
/// batOSAdminCenter (GET /api/global-settings) und cached ihn lokal
/// (<see cref="PlatformStateService"/>). V1-Scope: nur der Shutoff-Block.
///
/// Bewusste Designentscheidungen (identisch zum batOS-Core-Pattern):
/// - Push/Pull-Prinzip: Pull ist die einzige Wahrheitsquelle. Das AC pusht
///   bei Änderungen nur einen datenlosen Anstoß auf POST /api/platform/sync
///   (<see cref="IAdminCenterSyncTrigger"/>). Intervall = reiner Fallback.
/// - Auto-Discovery: erster Pull legt die Instanz im AC an (Upsert per
///   X-Instance-ShortName). Niemand pflegt Instanzen manuell.
/// - Fehlertolerant: jeder Loop-Durchgang fängt alle Exceptions.
///
/// Der Tenant-Abo-Sync (welcher floQ-Kunde hat welches Abo) kommt als
/// eigener Verarbeitungsschritt dazu, sobald der AC-seitige Endpoint-
/// Vertrag dafür steht — Einstiegspunkt ist <see cref="PullOnceAsync"/>.
/// </summary>
public class AdminCenterSyncService(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpFactory,
    IOptions<AdminCenterOptions> options,
    IAdminCenterSyncTrigger trigger,
    PlatformStateService platformState,
    ILogger<AdminCenterSyncService> log) : BackgroundService
{
    private readonly AdminCenterOptions _options = options.Value;

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

        // Erweiterungspunkt: Tenant-Abo-Sync, sobald AC-Endpoint-Vertrag steht.
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
