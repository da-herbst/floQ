using floQ.Domain.Tenants;

namespace floQ.Domain.Billing;

/// <summary>
/// Manuell erfasste Zahlung auf eine Ausgangsrechnung. Zahlungszustand einer
/// Rechnung wird IMMER aus der Summe der Payments abgeleitet (Open /
/// PartiallyPaid / Paid / Overpaid) — nie als Status am Beleg gespeichert.
/// Tenant-scoped (denormalisiert): auch Direktabfragen sind tenant-isoliert.
/// </summary>
public class Payment : TenantScopedEntity
{
    public int Id { get; set; }

    public int InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;

    public decimal Amount { get; set; }
    public DateTime PaidDateUtc { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.BankTransfer;

    /// <summary>Zahlungsreferenz (Verwendungszweck, Avis-Nr., …).</summary>
    public string? Reference { get; set; }
    public string? Note { get; set; }

    public Guid RecordedByUserId { get; set; }
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}
