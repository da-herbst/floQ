namespace floQ.Web.Tenancy;

/// <summary>
/// Pro Request: welcher Tenant ist aktiv? Wird von <see cref="TenantResolverMiddleware"/>
/// aus dem Auth-Cookie-Claim befüllt und in EF Global Query Filter
/// + SaveChanges-Override konsumiert.
///
/// IsResolved=false heißt: anonymer/eingeloggter-aber-tenantloser Request
/// (Login-Page, Auth-Endpoints). Queries auf TenantScopedEntity sind dann
/// strukturell leer (Filter Guid.Empty).
/// </summary>
public interface ITenantContext
{
    Guid TenantId { get; }
    bool IsResolved { get; }
    void SetTenant(Guid tenantId);
}

/// <summary>
/// Default-Implementierung. Scoped (= ein Objekt pro Request).
/// </summary>
public class TenantContext : ITenantContext
{
    public Guid TenantId { get; private set; } = Guid.Empty;
    public bool IsResolved => TenantId != Guid.Empty;

    public void SetTenant(Guid tenantId)
    {
        if (IsResolved && TenantId != tenantId)
            throw new InvalidOperationException(
                $"TenantContext bereits gesetzt auf {TenantId}, neuer Wert {tenantId} abgelehnt.");
        TenantId = tenantId;
    }
}
