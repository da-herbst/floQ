using floQ.Domain.Tenants;

namespace floQ.Domain.Settings;

/// <summary>
/// Verschlüsseltes Secret des Tenants (batOS-AppSecrets-Muster, tenant-scoped).
/// Wert ist DataProtection-verschlüsselt — nie Klartext in der DB. V1-Nutzer:
/// SMTP-Passwort (Provider "SMTP", Key "Password"); weitere Integrationen
/// (Banking etc.) docken später am selben Schema an.
/// Eine Row pro (Tenant, Provider, Key).
/// </summary>
public class TenantSecret : TenantScopedEntity
{
    public int Id { get; set; }

    public string Provider { get; set; } = "";
    public string Key { get; set; } = "";
    public string EncryptedValue { get; set; } = "";

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
