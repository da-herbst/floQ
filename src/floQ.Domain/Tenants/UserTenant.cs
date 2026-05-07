using floQ.Domain.Identity;

namespace floQ.Domain.Tenants;

/// <summary>
/// n:m-Zuordnung User &lt;-&gt; Tenant. Auch in Phase 1 schon n:m angelegt
/// (UI bleibt aber Solo-Flow), damit später ohne Schema-Migration ein zweiter
/// Tenant pro User möglich ist (Steuerberater-Use-Case).
/// </summary>
public class UserTenant
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    /// <summary>
    /// Wenn ein User mehrere Tenants hat: dieser wird beim Login standardmäßig aktiviert.
    /// Genau einer pro User muss true sein.
    /// </summary>
    public bool IsDefault { get; set; }

    public TenantRole Role { get; set; } = TenantRole.Owner;

    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;
}

public enum TenantRole
{
    /// <summary>Voller Zugriff inkl. Settings/Stammdaten/Löschung.</summary>
    Owner = 1,

    /// <summary>Voller Zugriff auf Belege, kein Settings/Tenant-Mgmt.</summary>
    Member = 2,

    /// <summary>Reine Lese-Rolle (Steuerberater-Einsicht).</summary>
    Viewer = 3
}
