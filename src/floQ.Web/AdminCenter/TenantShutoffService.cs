using floQ.Web.Data;

namespace floQ.Web.AdminCenter;

/// <summary>
/// In-Memory-Cache der Tenant-Shutoff-Zustände (Spiegel der Shutoff-Felder
/// auf <see cref="floQ.Domain.Tenants.Tenant"/>). Singleton — die
/// <see cref="TenantShutoffMiddleware"/> liest pro Request hieraus, ohne
/// DB-Roundtrip. Schreiber ist der <see cref="AdminCenterSyncService"/>
/// (Quelle der Wahrheit ist das AC), der nach jedem Sync
/// <see cref="Reload"/> ruft.
/// </summary>
public class TenantShutoffService(IServiceScopeFactory scopeFactory)
{
    public sealed record ShutoffState(string Reason, string At);

    private volatile IReadOnlyDictionary<Guid, ShutoffState> _shutoff =
        new Dictionary<Guid, ShutoffState>();

    /// <summary>Shutoff-Zustand eines Tenants, oder null wenn aktiv (Normalbetrieb).</summary>
    public ShutoffState? Get(Guid tenantId) =>
        _shutoff.TryGetValue(tenantId, out var state) ? state : null;

    /// <summary>Lädt alle stillgelegten Tenants neu in den Cache. Beim
    /// App-Start und nach jedem Sync aufrufen.</summary>
    public void Reload()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _shutoff = db.Tenants
            .Where(t => t.ShutoffActive)
            .ToDictionary(t => t.Id, t => new ShutoffState(t.ShutoffReason, t.ShutoffAt));
    }
}
