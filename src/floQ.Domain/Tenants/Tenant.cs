namespace floQ.Domain.Tenants;

/// <summary>
/// Mandant. Trennt Daten harter Kunden (z.B. zwei Steuerberater-Kanzleien)
/// per <see cref="TenantScopedEntity.TenantId"/> + EF Global Query Filter.
/// In Phase 1 läuft jeder Nutzer in genau einem auto-erzeugten Tenant.
/// </summary>
public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Anzeigename in der UI. In Phase 1 = Mail-Adresse des Erstellers.</summary>
    public string Name { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
