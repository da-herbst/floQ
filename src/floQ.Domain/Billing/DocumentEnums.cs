namespace floQ.Domain.Billing;

/// <summary>Ausgehende Belegtypen (Kern-Scope floQ). Werte stabil halten —
/// sie stecken in DocumentNumberConfig und im TPH-Diskriminator.</summary>
public enum DocumentType
{
    Quote = 1,
    Invoice = 2,
    CreditNote = 3,
    CancellationInvoice = 4,
    PaymentReminder = 5
}

/// <summary>Lebenszyklus eines ausgehenden Belegs.
/// Draft = editierbar/nummernlos; Created = abgeschlossen (Nummer gezogen,
/// PDF persistiert); Sent/Viewed = Versand-Tracking; Cancelled nur für
/// stornierte Rechnungen. „Bezahlt" ist KEIN Status — Zahlungszustand wird
/// aus der Payments-Summe abgeleitet (batOS-Prinzip).</summary>
public enum DocumentStatus
{
    Draft = 0,
    Created = 1,
    Sent = 2,
    Viewed = 3,
    Cancelled = 4
}

/// <summary>Steuerbefreiungs-Varianten. Bei ≠ None: keine USt im Beleg
/// (USt-Zeile wird trotzdem mit 0 % ausgewiesen), Pflichthinweis am PDF.
/// Positions-Steuersätze bleiben gespeichert (Zurückschalten verlustfrei).</summary>
public enum ReverseChargeMode
{
    None = 0,

    /// <summary>Reverse Charge EU-B2B (Art. 196 MwStSyst-RL / §19 Abs. 1 UStG).</summary>
    EuReverseCharge = 1,

    /// <summary>Drittland — nicht steuerbar in Österreich.</summary>
    ThirdCountry = 2,

    /// <summary>Kleinunternehmerregelung (§6 Abs. 1 Z 27 UStG) — unecht befreit.
    /// Default für neue Entwürfe, wenn <c>CompanyProfile.IsSmallBusiness</c> gesetzt ist.</summary>
    SmallBusiness = 3
}

/// <summary>Zahlungsweg einer manuell erfassten Zahlung.</summary>
public enum PaymentMethod
{
    BankTransfer = 1,
    Cash = 2,
    Card = 3,
    DirectDebit = 4,
    Other = 99
}

/// <summary>Sales-Zustand eines Angebots (unabhängig vom Beleg-Lebenszyklus).</summary>
public enum QuoteSalesStatus
{
    Open = 0,
    Commissioned = 1,
    Expired = 2,
    Cancelled = 3
}
