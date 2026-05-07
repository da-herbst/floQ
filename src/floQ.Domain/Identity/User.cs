namespace floQ.Domain.Identity;

/// <summary>
/// Plattform-User. Bewusst NICHT tenant-scoped: ein User kann später zu mehreren
/// Tenants gehören (z.B. Steuerberater bekommt Zugang zur Kanzlei eines Mandanten).
/// In Phase 1 hat jeder User genau einen Tenant — siehe <see cref="Tenants.UserTenant"/>.
/// </summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Login-Identifikator. Unique über alle User.</summary>
    public string Email { get; set; } = "";

    /// <summary>Anzeigename in UI/Mails. In Phase 1 = Email bis User es ändert.</summary>
    public string DisplayName { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAtUtc { get; set; }
}
