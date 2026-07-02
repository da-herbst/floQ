using floQ.Domain.Tenants;

namespace floQ.Domain.Billing;

/// <summary>Vor-/Endtext pro Belegtyp (auf dem PDF über/unter der Positionstabelle).
/// Eine Row pro (Tenant, DocumentType).</summary>
public class BillingText : TenantScopedEntity
{
    public int Id { get; set; }
    public DocumentType DocumentType { get; set; }
    public string IntroText { get; set; } = "";
    public string ClosingText { get; set; } = "";
}

/// <summary>Defaults + Textbausteine pro Mahnstufe (0 = Zahlungserinnerung, 1–3).
/// Eine Row pro (Tenant, Level).</summary>
public class ReminderLevelConfig : TenantScopedEntity
{
    public int Id { get; set; }
    public int Level { get; set; }
    public decimal DefaultFee { get; set; }
    /// <summary>Verzugszins in Prozent p.a. (null = keiner).</summary>
    public decimal? DefaultInterestRate { get; set; }
    public string IntroText { get; set; } = "";
    public string ClosingText { get; set; } = "";
}
