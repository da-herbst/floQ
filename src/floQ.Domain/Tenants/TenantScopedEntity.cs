namespace floQ.Domain.Tenants;

/// <summary>
/// Basis für jede Entity, die mandantenspezifisch ist (Belege, Kunden,
/// Stammdaten, Settings ...). EF konfiguriert für jeden Subtyp einen
/// Global Query Filter auf <see cref="TenantId"/>; SaveChanges-Override
/// setzt die Spalte beim Insert automatisch aus dem ITenantContext.
/// Wer eine Entity erbt, wird damit automatisch Tenant-isoliert.
/// </summary>
public abstract class TenantScopedEntity
{
    public Guid TenantId { get; set; }
}
