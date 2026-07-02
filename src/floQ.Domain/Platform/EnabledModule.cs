namespace floQ.Domain.Platform;

/// <summary>
/// Lokaler Cache der Modul-Abos eines Mandanten aus dem batOSAdminCenter
/// (jeder Tenant ist dort eine eigene Instanz). Das AC ist Source of Truth —
/// der AdminCenterSyncService spiegelt je Tenant die aktive Abo-Liste in
/// diese Tabelle (aktive Keys upserten, nicht mehr gemeldete löschen).
/// Das Modul-Gating liest ausschließlich diesen Cache (über den In-Memory-
/// Read-Through im ModuleGateService), damit ein AC-Ausfall die
/// Modul-Verfügbarkeit nicht kippt.
/// Bewusst KEINE TenantScopedEntity: Plattform-Schicht, der Sync läuft
/// ohne Request-TenantContext.
/// </summary>
public class EnabledModule
{
    public Guid TenantId { get; set; }

    /// <summary>Stabiler Modul-Key, identisch mit dem AC-Abo (lowercase, ≤ 64).</summary>
    public string Key { get; set; } = "";

    /// <summary>Letzter Zeitpunkt, zu dem das AC dieses Modul als aktiv
    /// gemeldet hat. Wird bei jedem Sync neu gestempelt.</summary>
    public DateTime LastSeenActiveAtUtc { get; set; }
}
