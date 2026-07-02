using floQ.Domain.Platform;
using floQ.Web.Data;

namespace floQ.Web.AdminCenter;

/// <summary>
/// In-Memory-Cache über der <see cref="EnabledModule"/>-Tabelle (Abo-Cache
/// aus dem AC). Singleton — Gating und Nav lesen pro Request hieraus, ohne
/// DB-Roundtrip und nie per Live-Call ans AC. Schreiber ist ausschließlich
/// der <see cref="AdminCenterSyncService"/>, der nach jedem Abo-Sync
/// <see cref="Reload"/> ruft.
///
/// Aktiv = im AC abonniert UND im lokalen <see cref="ModuleCatalog"/>
/// bekannt (veraltete Abos nach Modul-Entfernung greifen nicht).
///
/// Dev-Override: in Development gelten mit
/// <c>"Modules": { "EnableAllInDevelopment": true }</c> alle Katalog-Module
/// als aktiv — ohne DB-Einträge. Die Freischaltungs-Hoheit bleibt beim AC;
/// der Override ist in Production wirkungslos (Env-Check + Config-Flag).
/// </summary>
public class ModuleGateService(
    IServiceScopeFactory scopeFactory,
    ModuleCatalog catalog,
    IWebHostEnvironment env,
    IConfiguration config)
{
    private volatile IReadOnlySet<string> _active =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private bool DevEnableAll =>
        env.IsDevelopment() && config.GetValue<bool>("Modules:EnableAllInDevelopment");

    public bool IsActive(string key) =>
        ActiveKeys.Contains(key);

    /// <summary>Alle effektiv aktiven Modul-Keys (case-insensitives Set).</summary>
    public IReadOnlySet<string> ActiveKeys =>
        DevEnableAll
            ? catalog.All.Select(m => m.Key).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : _active;

    /// <summary>Lädt den Abo-Cache neu aus der DB. Beim App-Start und nach
    /// jedem Abo-Sync aufrufen.</summary>
    public void Reload()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _active = db.EnabledModules
            .Select(e => e.Key)
            .AsEnumerable()
            .Where(k => catalog.FindByKey(k) is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
