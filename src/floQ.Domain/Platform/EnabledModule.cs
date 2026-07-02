namespace floQ.Domain.Platform;

/// <summary>
/// Lokaler Cache der Modul-Abos dieser Instanz aus dem batOSAdminCenter.
/// Das AC ist Source of Truth — der AdminCenterSyncService spiegelt die
/// aktive Abo-Liste periodisch in diese Tabelle (aktive Keys upserten,
/// nicht mehr gemeldete löschen). Das Modul-Gating liest ausschließlich
/// diesen Cache (über den In-Memory-Read-Through im ModuleGateService),
/// damit ein AC-Ausfall die Modul-Verfügbarkeit nicht kippt.
/// Bewusst NICHT tenant-scoped: ein Abo gilt für die gesamte Instanz.
/// </summary>
public class EnabledModule
{
    /// <summary>Stabiler Modul-Key, identisch mit dem AC-Abo (lowercase, ≤ 64).</summary>
    public string Key { get; set; } = "";

    /// <summary>Letzter Zeitpunkt, zu dem das AC dieses Modul als aktiv
    /// gemeldet hat. Wird bei jedem Sync neu gestempelt.</summary>
    public DateTime LastSeenActiveAtUtc { get; set; }
}
