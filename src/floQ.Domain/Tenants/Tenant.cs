namespace floQ.Domain.Tenants;

/// <summary>
/// Mandant. Trennt Daten harter Kunden (z.B. zwei Steuerberater-Kanzleien)
/// per <see cref="TenantScopedEntity.TenantId"/> + EF Global Query Filter.
/// In Phase 1 läuft jeder Nutzer in genau einem auto-erzeugten Tenant.
///
/// Jeder Tenant ist zugleich eine Instanz im batOSAdminCenter (Software
/// "floq"): <see cref="Slug"/> ist der AC-ShortName, die Shutoff-Felder
/// sind der lokale Cache des AC-Zustands (Quelle der Wahrheit ist das AC,
/// gespiegelt vom AdminCenterSyncService).
/// </summary>
public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Anzeigename in der UI. In Phase 1 = Mail-Adresse des Erstellers.</summary>
    public string Name { get; set; } = "";

    /// <summary>Stabiler Mandanten-Schlüssel, zugleich ShortName der
    /// AC-Instanz (lowercase, [a-z0-9-], ≤ 32, unique). Einmal vergeben,
    /// nie wieder ändern — das AC upsertet Instanzen über diesen Wert.</summary>
    public string Slug { get; set; } = "";

    /// <summary>true = das AC hat diesen Mandanten stillgelegt →
    /// 503-Wartungsseite für alle seine Requests.</summary>
    public bool ShutoffActive { get; set; }

    public string ShutoffReason { get; set; } = "";

    /// <summary>Roh-Timestamp aus dem AC (ISO-8601-String, wie geliefert).</summary>
    public string ShutoffAt { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
