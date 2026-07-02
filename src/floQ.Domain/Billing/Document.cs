using floQ.Domain.Tenants;

namespace floQ.Domain.Billing;

/// <summary>
/// Basis aller ausgehenden Belege (TPH: eine Tabelle, Diskriminator pro Subtyp).
/// Nach batOS-DocumentEngine-Vorbild, tenant-scoped.
///
/// Kernprinzipien (geerbt aus batOS, Letztstand 2026-07):
/// - <b>Autarker Beleg</b>: Der Empfänger-Snapshot (Recipient*) am Beleg ist die
///   Wahrheit fürs PDF. Der Kundenstamm (<see cref="Customer"/>) ist nur
///   „Adressbuch"-Befüllung. Folge-Belege kopieren den Snapshot vom Original.
/// - <b>Nummer erst beim Abschluss</b>: Entwürfe sind nummernlos; die Belegnummer
///   wird bei Finalize race-safe gezogen (SELECT … FOR UPDATE). Verworfene
///   Entwürfe reißen keine Lücken in die fortlaufende Nummernfolge (§11 UStG).
/// - <b>Steuerbefreiung</b>: <see cref="ReverseChargeMode"/> ≠ None ⇒ USt = 0,
///   USt-Zeile wird trotzdem ausgewiesen (0 %), Pflichthinweis am PDF.
/// </summary>
public abstract class Document : TenantScopedEntity
{
    public int Id { get; set; }

    /// <summary>Belegnummer — leer bis zum Abschluss (nummernlose Entwürfe).</summary>
    public string Number { get; set; } = "";

    /// <summary>Belegdatum (UTC gespeichert, Wien angezeigt).</summary>
    public DateTime Date { get; set; }

    /// <summary>Bruttobetrag — beim Speichern aus den Positionen berechnet
    /// (bei Reverse Charge ohne USt-Aufschlag).</summary>
    public decimal Gross { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

    /// <summary>Optionale Verknüpfung zum Kundenstamm (Befüllung, nie Wahrheit).</summary>
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    // Empfänger-Snapshot (autarker Beleg)
    public string? RecipientName { get; set; }
    public string? RecipientAddress { get; set; }
    public string? RecipientZip { get; set; }
    public string? RecipientCity { get; set; }
    public string? RecipientCountry { get; set; }
    public string? RecipientUid { get; set; }
    public string? RecipientEmail { get; set; }

    public bool HasRecipientSnapshot => !string.IsNullOrWhiteSpace(RecipientName);

    // Steuerbefreiung
    public ReverseChargeMode ReverseChargeMode { get; set; } = ReverseChargeMode.None;
    /// <summary>Pro-Beleg-Wortlaut des Befreiungs-Hinweises (null = Default je Modus).</summary>
    public string? ReverseChargeNote { get; set; }

    // Zahlungskonditionen
    public int? PaymentTermDays { get; set; }
    public int? PaymentTermDiscountDays { get; set; }
    /// <summary>Skonto in Prozent (3 = 3 %).</summary>
    public decimal? DiscountRate { get; set; }

    public string? Note { get; set; }

    /// <summary>Pfad des beim Abschluss persistierten PDFs (relativ zum
    /// Tenant-Upload-Root). Null solange Draft bzw. nach Unlock.</summary>
    public string? PdfPath { get; set; }

    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<DocumentEntry> Entries { get; set; } = [];

    /// <summary>Fälligkeitsdatum aus Belegdatum + Zahlungsziel (null ohne Ziel).</summary>
    public DateTime? DueDate => PaymentTermDays.HasValue ? Date.AddDays(PaymentTermDays.Value) : null;
}

/// <summary>Angebot.</summary>
public class Quote : Document
{
    public DateTime? ServiceDate { get; set; }
    public DateTime? ServicePeriodStart { get; set; }
    public DateTime? ServicePeriodEnd { get; set; }
    public DateTime? ValidUntil { get; set; }
    public string? ExternalReference { get; set; }
    /// <summary>Freitext-Konditionen (unter der Positionstabelle gedruckt).</summary>
    public string? ConditionNotes { get; set; }
    public QuoteSalesStatus SalesStatus { get; set; } = QuoteSalesStatus.Open;
}

/// <summary>Ausgangsrechnung. §11 UStG: Leistungszeitraum ist Pflicht beim Abschluss.</summary>
public class Invoice : Document
{
    public DateTime? ServiceDate { get; set; }
    public DateTime? ServicePeriodStart { get; set; }
    public DateTime? ServicePeriodEnd { get; set; }

    public List<Payment> Payments { get; set; } = [];
}

/// <summary>Gutschrift zu einer Ausgangsrechnung.</summary>
public class CreditNote : Document
{
    /// <summary>Original-Rechnung (0 = noch nicht gewählt, Pflicht beim Abschluss).</summary>
    public int OriginalInvoiceId { get; set; }
}

/// <summary>Stornorechnung — Gegenbuchung zur Original-AR (gespiegelte Positionen,
/// negativer Betrag). Abschluss setzt das Original auf Cancelled.</summary>
public class CancellationInvoice : Document
{
    public int OriginalInvoiceId { get; set; }
}

/// <summary>Mahnung (Stufe 0 = Zahlungserinnerung, 1–3 = Mahnungen).
/// Brutto = Summe offener Beträge + Mahngebühr + Verzugszinsen.</summary>
public class PaymentReminder : Document
{
    public int ReminderLevel { get; set; }
    public DateTime? ReminderDueDate { get; set; }
    public decimal ReminderFee { get; set; }
    public decimal? InterestRate { get; set; }
    public decimal? InterestAmount { get; set; }

    public List<ReminderInvoice> ReminderInvoices { get; set; } = [];
}

/// <summary>n:m Mahnung ↔ gemahnte Rechnungen mit Snapshot des offenen Betrags.
/// Tenant-scoped (denormalisiert): auch Direktabfragen sind tenant-isoliert.</summary>
public class ReminderInvoice : TenantScopedEntity
{
    public int PaymentReminderId { get; set; }
    public PaymentReminder PaymentReminder { get; set; } = null!;

    public int InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;

    /// <summary>Offener Betrag zum Mahnzeitpunkt (Brutto − Gutschriften − Zahlungen).</summary>
    public decimal OutstandingAmount { get; set; }
}
