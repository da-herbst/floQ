using floQ.Domain.Billing;

namespace floQ.Web.Services.Documents;

// ══════════════════════════════════════════════════════════════════
// DTOs des Beleg-Vertrags
// ══════════════════════════════════════════════════════════════════

/// <summary>Allgemeines Operations-Ergebnis der Engine.</summary>
public sealed class DocumentResult
{
    public bool Success { get; init; }
    public int DocumentId { get; init; }
    public List<string> Errors { get; init; } = [];
    public string? Error => Errors.Count > 0 ? string.Join(" · ", Errors) : null;

    public static DocumentResult Ok(int id) => new() { Success = true, DocumentId = id };
    public static DocumentResult Fail(params string[] errors) => new() { Errors = [.. errors] };
    public static DocumentResult Fail(List<string> errors) => new() { Errors = errors };
}

/// <summary>Eine Belegposition (Lesen und Schreiben). Freitext ohne
/// Artikel-Zwang (floQ-Entscheidung). Die Speicher-Reihenfolge ist die
/// Listen-Reihenfolge — <see cref="ParentEntryIndex"/> referenziert den
/// Voll-Array-Index der Hauptposition (batOS-Konvention).</summary>
public sealed class DocumentEntryDto
{
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal VatRate { get; set; }
    /// <summary>Einheit als Freitext ("Std.", "Stk.", "pauschal", …).</summary>
    public string Unit { get; set; } = "";
    /// <summary>Rabattzeile: Voll-Array-Index der Hauptposition (null = Hauptposition).</summary>
    public int? ParentEntryIndex { get; set; }
    /// <summary>Eingabemodus der Rabattzeile: null = Euro, Wert = Prozent.</summary>
    public decimal? DiscountPercent { get; set; }

    public decimal Net => Quantity * UnitPrice;
    public bool IsDiscount => ParentEntryIndex.HasValue;
}

public sealed record ReminderInvoiceRef(int InvoiceId, decimal OutstandingAmount);

/// <summary>
/// Editierzustand eines Draft-Belegs — typ-generisch, typ-spezifische Felder
/// sind nullable und gelten nur für den jeweiligen <see cref="Type"/>.
/// Datums-Werte in Wien-Zeit (UI-Vertrag); die Engine konvertiert nach UTC.
/// </summary>
public sealed class DocumentDraftDto
{
    public int Id { get; set; }
    public DocumentType Type { get; set; }
    /// <summary>Nur Anzeige — Save übernimmt die Nummer NIE (Nummer erst beim Abschluss).</summary>
    public string? Number { get; set; }
    public DateTime DocumentDateVienna { get; set; }
    public int? CustomerId { get; set; }
    public string? Note { get; set; }
    public int? PaymentTermDays { get; set; }
    public int? PaymentTermDiscountDays { get; set; }
    public decimal? DiscountRate { get; set; }

    /// <summary>Steuerbefreiung: Beleg ohne USt (None = normal besteuert).</summary>
    public ReverseChargeMode ReverseChargeMode { get; set; } = ReverseChargeMode.None;
    /// <summary>Pro-Beleg-Wortlaut des Hinweises (leer = Default je Modus).</summary>
    public string? ReverseChargeNote { get; set; }

    // Empfänger-Snapshot (autarker Beleg — der Kundenstamm ist nur Befüllung,
    // die Felder am Beleg sind die Wahrheit fürs PDF)
    public string? RecipientName { get; set; }
    public string? RecipientAddress { get; set; }
    public string? RecipientZip { get; set; }
    public string? RecipientCity { get; set; }
    public string? RecipientCountry { get; set; }
    public string? RecipientUid { get; set; }
    public string? RecipientEmail { get; set; }

    // Quote + Invoice
    public DateTime? ServiceDateVienna { get; set; }
    public DateTime? ServicePeriodStartVienna { get; set; }
    public DateTime? ServicePeriodEndVienna { get; set; }

    // Quote
    public DateTime? ValidUntilVienna { get; set; }
    public string? ExternalReference { get; set; }
    public string? ConditionNotes { get; set; }

    // CreditNote / CancellationInvoice
    public int? OriginalInvoiceId { get; set; }
    /// <summary>Brutto-Override (nur CreditNote; null = aus Entries berechnen).</summary>
    public decimal? GrossOverride { get; set; }

    // PaymentReminder
    public int ReminderLevel { get; set; }
    public DateTime? ReminderDueDateVienna { get; set; }
    public decimal ReminderFee { get; set; }
    public decimal? InterestRate { get; set; }
    public decimal? InterestAmount { get; set; }
    public List<ReminderInvoiceRef> ReminderInvoices { get; set; } = [];

    public List<DocumentEntryDto> Entries { get; set; } = [];
}

/// <summary>Kopf-/Statusdaten eines Belegs für Aside und Listen-Drill-in.</summary>
public sealed class DocumentDetailDto
{
    public int Id { get; init; }
    public DocumentType Type { get; init; }
    /// <summary>Deutscher Typ-Name ("Angebot", "1. Mahnung", …).</summary>
    public string TypeName { get; init; } = string.Empty;
    public string Number { get; init; } = string.Empty;
    public DocumentStatus Status { get; init; }
    public DateTime DateVienna { get; init; }
    public decimal Gross { get; init; }
    public int? CustomerId { get; init; }
    public string? CustomerName { get; init; }
    /// <summary>Versand-Default: Empfänger-Snapshot, sonst Kundenstamm.</summary>
    public string? RecipientEmail { get; init; }
    public int? OriginalInvoiceId { get; init; }
    public string? OriginalInvoiceNumber { get; init; }
    /// <summary>Nur Invoice: existiert bereits eine Stornorechnung?</summary>
    public bool HasExistingCancellation { get; init; }
    /// <summary>Nur Invoice: höchste bereits angelegte Mahnstufe (null = keine).</summary>
    public int? MaxReminderLevel { get; init; }
}

/// <summary>Zeile der Beleg-Übersicht.</summary>
public sealed class DocumentListRow
{
    public int Id { get; init; }
    public DocumentType Type { get; init; }
    public string Number { get; init; } = string.Empty;
    public DateTime DateVienna { get; init; }
    public int? CustomerId { get; init; }
    public string CustomerName { get; init; } = "–";
    public decimal Gross { get; init; }
    public decimal Net { get; init; }
    public decimal Vat { get; init; }
    public decimal SumPaid { get; init; }
    public decimal Remaining { get; init; }
    /// <summary>Nur Invoice: Open | PartiallyPaid | Paid | Overpaid.</summary>
    public string? PaymentState { get; init; }
    public DocumentStatus Status { get; init; }
    public int? ReminderLevel { get; init; }
    public DateTime? DueDateVienna { get; init; }
    public string Note { get; init; } = string.Empty;
    public int? PaymentTermDays { get; init; }
    public string? ServicePeriod { get; init; }
    public string? ValidUntil { get; init; }
    public QuoteSalesStatus? SalesStatus { get; init; }
    public bool HasExistingCancellation { get; init; }
    public int? MaxReminderLevel { get; init; }
    public List<DocumentEntryDto> Entries { get; init; } = [];
}

/// <summary>Filter der Beleg-Übersicht. Leeres <see cref="Types"/> = alle
/// fünf Belegtypen (floQ ist Owner-only, kein Permission-Filter).</summary>
public sealed class DocumentListFilter
{
    public IReadOnlySet<DocumentType> Types { get; init; } = new HashSet<DocumentType>();
    public int? CustomerId { get; init; }
    /// <summary>Einzelner Beleg (Detail-Fetch der Liste) — liefert genau die eine reiche Zeile.</summary>
    public int? Id { get; init; }
}

/// <summary>Auswahllisten für den Beleg-Editor (eine Ladung pro Page-Hit).</summary>
public sealed class DocumentEditorContext
{
    public List<(int Id, string Label)> Customers { get; init; } = [];
    public List<(int Id, string Label)> Invoices { get; init; } = [];
}

public sealed record OpenInvoiceDto(
    int Id, string Number, DateTime DateVienna, decimal Gross, decimal SumPaid,
    decimal Outstanding, DateTime? DueDateVienna, bool IsOverdue);

public sealed record ReminderLevelDefaults(decimal Fee, decimal? InterestRate, string IntroText, string ClosingText);

public sealed record NumberResult(bool Success, string? Number, string? Error);

public sealed record PaymentRow(
    int Id, DateTime PaidDateVienna, decimal Amount, PaymentMethod Method,
    string MethodLabel, string? Reference, string? Note);

public sealed record PdfResult(byte[] Bytes, string FileName);

// ══════════════════════════════════════════════════════════════════
// Vertrag
// ══════════════════════════════════════════════════════════════════

/// <summary>
/// Beleg-Engine — EIN typ-generischer Vertrag für das Erstellen, Bearbeiten
/// und Abschließen ausgehender Belege (Angebot, Rechnung, Gutschrift,
/// Stornorechnung, Mahnung). Port des batOS-DocumentEngine-Letztstands,
/// vereinfacht um Job-/Artikel-/Permission-Kopplung (floQ ist Owner-only,
/// Positionen sind Freitext).
///
/// Tenant-Isolation kommt aus dem EF Global Query Filter + Auto-Stamping —
/// die Engine selbst kennt keine TenantId (Ausnahme: der Nummernzug, dessen
/// Row-Lock als Raw-SQL am Filter vorbeigeht).
///
/// PDF-Persistierung (beim Abschluss) und Versand folgen als eigene
/// Ausbauschritte — Einstiegspunkte: FinalizeAsync / UnlockAsync.
/// </summary>
public interface IDocumentEngine
{
    // ── Lesen ────────────────────────────────────────────────────────
    Task<List<DocumentListRow>> GetListAsync(DocumentListFilter filter, CancellationToken ct = default);
    Task<DocumentDetailDto?> GetDetailAsync(int id, CancellationToken ct = default);
    Task<DocumentDraftDto?> GetDraftAsync(int id, CancellationToken ct = default);
    Task<DocumentEditorContext> GetEditorContextAsync(CancellationToken ct = default);
    Task<List<OpenInvoiceDto>> GetOpenInvoicesAsync(int customerId, CancellationToken ct = default);
    Task<ReminderLevelDefaults?> GetReminderDefaultsAsync(int level, CancellationToken ct = default);
    Task<NumberResult> PeekNextNumberAsync(DocumentType type, CancellationToken ct = default);
    Task<List<PaymentRow>> GetPaymentsAsync(int invoiceId, CancellationToken ct = default);

    /// <summary>Read-only-Query-Wurzel (no-tracking) — deklarierter Lesepfad
    /// für künftige Auswertungs-Konsumenten.</summary>
    IQueryable<Document> DocumentsQuery { get; }

    // ── Anlegen ──────────────────────────────────────────────────────
    /// <summary>Leeren Draft anlegen (nummernlos — Belegnummer erst beim Abschluss).</summary>
    Task<DocumentResult> CreateDraftAsync(DocumentType type, Guid userId, CancellationToken ct = default);
    /// <summary>Stornorechnungs-Draft aus einer Rechnung (gespiegelte Positionen, negativer Betrag).</summary>
    Task<DocumentResult> CreateCancellationFromInvoiceAsync(int originalInvoiceId, Guid userId, CancellationToken ct = default);
    /// <summary>Gutschrift-Draft aus einer Rechnung (kopierte Positionen).</summary>
    Task<DocumentResult> CreateCreditNoteFromInvoiceAsync(int originalInvoiceId, Guid userId, CancellationToken ct = default);
    /// <summary>Mahnungs-Draft aus einer Rechnung (Stufe 0–3, Stufen-Defaults).</summary>
    Task<DocumentResult> CreateReminderFromInvoiceAsync(int sourceInvoiceId, int level, Guid userId, CancellationToken ct = default);
    /// <summary>Weiterverarbeiten: neuen Entwurf des Zieltyps aus einer Quelle erzeugen
    /// (Kopf + Positionen vorausgefüllt, danach editierbar).</summary>
    Task<DocumentResult> CreateDraftFromSourceAsync(int sourceId, DocumentType targetType, Guid userId, CancellationToken ct = default);

    // ── Bearbeiten ───────────────────────────────────────────────────
    /// <summary>Draft speichern (validiert; ersetzt Entries, Draft-only).
    /// Belegnummer wird NICHT aus dem Draft übernommen (Nummer erst beim Abschluss).</summary>
    Task<DocumentResult> SaveDraftAsync(DocumentDraftDto draft, CancellationToken ct = default);
    /// <summary>Draft verwerfen (Beleg + Kinder löschen; Counter bleibt — verworfene
    /// Entwürfe reißen keine Lücken, weil sie nie eine Nummer hatten).</summary>
    Task<DocumentResult> DiscardDraftAsync(int id, CancellationToken ct = default);
    /// <summary>Beleg löschen (Listen-Pfad).</summary>
    Task<DocumentResult> DeleteAsync(int id, CancellationToken ct = default);

    // ── Lebenszyklus ─────────────────────────────────────────────────
    /// <summary>Abschließen: Draft → Created. Kaskaden: Nummer ziehen (race-safe),
    /// Storno setzt Original-Rechnung auf Cancelled. PDF-Persistierung folgt
    /// mit der PDF-Pipeline.</summary>
    Task<DocumentResult> FinalizeAsync(int id, Guid userId, CancellationToken ct = default);
    /// <summary>Entsperren: Created → Draft (persistiertes PDF wird verworfen).</summary>
    Task<DocumentResult> UnlockAsync(int id, CancellationToken ct = default);

    // ── PDF ──────────────────────────────────────────────────────────
    /// <summary>Beleg-PDF rendern (Briefpapier-Overlay aus dem CompanyProfile).
    /// <paramref name="requireFinalized"/> blockiert Entwürfe (Download-Pfad);
    /// false = Live-Vorschau.</summary>
    Task<PdfResult?> RenderPdfAsync(int id, bool requireFinalized, CancellationToken ct = default);

    // ── Zahlungen ────────────────────────────────────────────────────
    /// <summary>Manuelle Zahlung auf eine Rechnung erfassen.</summary>
    Task<DocumentResult> RecordPaymentAsync(int invoiceId, decimal amount, DateTime paidDateVienna,
        PaymentMethod method, string? reference, string? note, Guid userId, CancellationToken ct = default);
    /// <summary>Zahlung löschen (Korrektur-Pfad).</summary>
    Task<DocumentResult> DeletePaymentAsync(int paymentId, CancellationToken ct = default);
}
