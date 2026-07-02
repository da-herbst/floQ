namespace floQ.Domain.Billing;

/// <summary>
/// Default-Wortlaute des Steuerbefreiungs-Hinweises je Variante (batOS-Letztstand).
/// Der Hinweis wird bei <see cref="ReverseChargeMode"/> ≠ None IMMER am PDF
/// ausgewiesen; <see cref="Document.ReverseChargeNote"/> überschreibt pro Beleg.
/// </summary>
public static class ReverseChargeNoteDefaults
{
    public const string Eu =
        "Steuerschuldnerschaft des Leistungsempfängers (Reverse Charge). Die Umsatzsteuer ist vom Leistungsempfänger zu entrichten.";

    public const string ThirdCountry =
        "Nicht steuerbare Leistung – Leistungsort im Ausland (§ 3a Abs 6 UStG). Besteuerung im Empfängerland.";

    public const string SmallBusiness =
        "Umsatzsteuerbefreit gemäß § 6 Abs. 1 Z 27 UStG (Kleinunternehmerregelung).";

    public static string Builtin(ReverseChargeMode mode) => mode switch
    {
        ReverseChargeMode.EuReverseCharge => Eu,
        ReverseChargeMode.ThirdCountry => ThirdCountry,
        ReverseChargeMode.SmallBusiness => SmallBusiness,
        _ => string.Empty
    };
}
