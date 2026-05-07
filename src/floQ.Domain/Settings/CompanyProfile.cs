using floQ.Domain.Tenants;

namespace floQ.Domain.Settings;

/// <summary>
/// Stammdaten des Rechnungs-Ausstellers (= "meine Firma" pro Tenant).
/// Wird beim Tenant-Anlegen automatisch leer angelegt; User füllt aus
/// unter /Settings/CompanyProfile vor der ersten Rechnung.
/// Genau eine Row pro Tenant.
/// </summary>
public class CompanyProfile : TenantScopedEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string LegalName { get; set; } = "";
    public string Street { get; set; } = "";
    public string ZipCode { get; set; } = "";
    public string City { get; set; } = "";

    /// <summary>ISO-3166-Alpha-2 Country Code, default AT.</summary>
    public string CountryCode { get; set; } = "AT";

    /// <summary>UID-Nummer (z.B. "ATU12345678"). Pflicht für §11-konforme Rechnungen.</summary>
    public string? VatId { get; set; }

    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Website { get; set; }

    /// <summary>Primäre Bankverbindung (auf Rechnung gedruckt). Mehrere Konten kommen später.</summary>
    public string? Iban { get; set; }
    public string? Bic { get; set; }
    public string? BankName { get; set; }

    /// <summary>Pfad zu Logo unter /uploads/{tenantId}/branding/logo.{ext}.</summary>
    public string? LogoPath { get; set; }

    /// <summary>Pfad zu Vektor-Briefpapier unter /uploads/{tenantId}/letterhead/briefpapier.pdf.</summary>
    public string? LetterheadPdfPath { get; set; }

    /// <summary>
    /// Kleinunternehmerregelung §6 Abs. 1 Z 27 UStG. Wenn true: keine USt ausweisen,
    /// stattdessen Befreiungs-Hinweis (siehe <see cref="TaxExemptionText"/>).
    /// </summary>
    public bool IsSmallBusiness { get; set; }

    /// <summary>
    /// Frei formulierter Steuerbefreiungs-/Reverse-Charge-Hinweistext.
    /// Wird auf der Rechnung im Fußbereich gedruckt. Pflicht wenn IsSmallBusiness=true
    /// oder Reverse-Charge-Position existiert.
    /// </summary>
    public string? TaxExemptionText { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
