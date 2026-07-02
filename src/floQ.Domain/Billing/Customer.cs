using floQ.Domain.Tenants;

namespace floQ.Domain.Billing;

/// <summary>
/// Kundenstamm (Rechnungsempfänger) — reines „Adressbuch": befüllt den
/// Empfänger-Snapshot am Beleg, ist aber nie die Wahrheit fürs PDF
/// (Autark-Prinzip, siehe <see cref="Document"/>).
/// </summary>
public class Customer : TenantScopedEntity
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? Zip { get; set; }
    public string? City { get; set; }

    /// <summary>ISO-3166-Alpha-2, default AT.</summary>
    public string CountryCode { get; set; } = "AT";

    /// <summary>UID-Nummer. Für Reverse-Charge-Belege Pflicht (§11 Abs. 1a UStG).</summary>
    public string? VatId { get; set; }

    /// <summary>Letztes VIES-Prüfergebnis (Audit: UID war am Prüfdatum gültig).</summary>
    public bool? VatIdValidated { get; set; }
    public DateTime? VatIdCheckedAtUtc { get; set; }

    public string? Email { get; set; }
    public string? Phone { get; set; }

    public string? Note { get; set; }
    public bool IsArchived { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
