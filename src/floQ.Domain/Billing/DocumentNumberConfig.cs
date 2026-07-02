using floQ.Domain.Tenants;

namespace floQ.Domain.Billing;

/// <summary>
/// Belegnummern-Zähler pro Tenant, Belegtyp und Geschäftsjahr.
/// Format (batOS-Konvention): <c>YYYY{Sep}CC{Sep}NNNN</c> — Jahr, zweistelliger
/// Typ-Code, links genullter Zähler. Beispiel <c>2026-11-0042</c>.
///
/// Lückenlosigkeit: Der Zähler wird ausschließlich beim Abschluss (Finalize)
/// in einer Transaktion mit SELECT … FOR UPDATE auf diese Zeile inkrementiert —
/// parallele Abschlüsse werden serialisiert, keine Nummer doppelt, keine Lücke.
/// </summary>
public class DocumentNumberConfig : TenantScopedEntity
{
    public int Id { get; set; }

    public DocumentType DocumentType { get; set; }

    /// <summary>Typ-Code in der Belegnummer (Default: 10=AN, 11=RE, 12=GS, 13=SR, 14=MA).</summary>
    public int TypeCode { get; set; }

    public int Year { get; set; }

    /// <summary>Letzte vergebene Sequenz (0 = noch keine).</summary>
    public int CurrentCounter { get; set; }

    /// <summary>Trenner zwischen Jahr / TypCode / Sequenz ("" = kein Trenner).</summary>
    public string Separator { get; set; } = "-";

    /// <summary>Stellen des Sequenzzählers (links genullt), 1–8.</summary>
    public int SequencePadding { get; set; } = 4;

    /// <summary>Kanonisches Nummern-Format — Single Source of Truth.</summary>
    public string Format(int sequence)
    {
        var pad = SequencePadding < 1 ? 4 : SequencePadding;
        return $"{Year}{Separator}{TypeCode:D2}{Separator}{sequence.ToString().PadLeft(pad, '0')}";
    }

    /// <summary>Default-TypeCodes je Belegtyp (beim Auto-Anlegen eines Jahres).</summary>
    public static int DefaultTypeCode(DocumentType type) => type switch
    {
        DocumentType.Quote => 10,
        DocumentType.Invoice => 11,
        DocumentType.CreditNote => 12,
        DocumentType.CancellationInvoice => 13,
        DocumentType.PaymentReminder => 14,
        _ => 99
    };
}
