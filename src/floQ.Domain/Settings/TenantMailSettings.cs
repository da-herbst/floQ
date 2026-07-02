using floQ.Domain.Tenants;

namespace floQ.Domain.Settings;

/// <summary>
/// SMTP-Konfiguration des Tenants (batOS-MailSettings-Muster, tenant-scoped):
/// floQ verschickt Belege über den Mail-Server des floQ-Kunden — White-Label
/// bis in den Mail-Header. Klartext-Felder hier; das Passwort liegt
/// verschlüsselt in <see cref="TenantSecret"/> (Provider "SMTP", Key "Password").
/// Eine Row pro Tenant.
/// </summary>
public class TenantMailSettings : TenantScopedEntity
{
    public int Id { get; set; }

    public string Host { get; set; } = "";
    /// <summary>465 = implizites SSL, 587 = STARTTLS.</summary>
    public int Port { get; set; } = 587;
    public string UserName { get; set; } = "";
    /// <summary>Absender-Adresse (From).</summary>
    public string Sender { get; set; } = "";
    /// <summary>Anzeigename des Absenders (Fallback: Firmenname aus dem CompanyProfile).</summary>
    public string? SenderDisplayName { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Vollständig konfiguriert? (Host + Sender sind Pflicht zum Versand.)</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Sender);
}
