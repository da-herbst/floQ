using floQ.Domain.Tenants;

namespace floQ.Domain.Billing;

/// <summary>
/// Belegposition — EINE Tabelle für alle Belegtypen (Vereinfachung gegenüber
/// batOS' per-Typ-Entry-Tabellen; der TPH-Parent ist ohnehin eine Tabelle).
/// Positionen sind Freitext-fähig — kein Artikel-Zwang (floQ-Entscheidung:
/// Onboarding ohne Stammdaten-Pflicht).
///
/// Rabattzeilen: <see cref="ParentEntryIndex"/> referenziert den Voll-Array-
/// Index der Hauptposition (batOS-Konvention); Rabatt-UnitPrice ist negativ.
///
/// Tenant-scoped (denormalisiert zusätzlich zum Parent): Direktabfragen auf
/// Positionen sind damit ebenfalls automatisch tenant-isoliert.
/// </summary>
public class DocumentEntry : TenantScopedEntity
{
    public int Id { get; set; }

    public int DocumentId { get; set; }
    public Document Document { get; set; } = null!;

    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    /// <summary>USt-Satz in Prozent (20 = 20 %). Bleibt bei Reverse Charge
    /// gespeichert, wird dort nur nicht ausgewiesen.</summary>
    public decimal VatRate { get; set; }

    /// <summary>Einheit als Freitext ("Std.", "Stk.", "pauschal", …).</summary>
    public string Unit { get; set; } = "";

    /// <summary>Rabattzeile: Voll-Array-Index der Hauptposition (null = Hauptposition).</summary>
    public int? ParentEntryIndex { get; set; }

    /// <summary>Eingabemodus der Rabattzeile: null = Euro, Wert = Prozent.</summary>
    public decimal? DiscountPercent { get; set; }

    public int SortOrder { get; set; }

    public decimal Net => Quantity * UnitPrice;
    public bool IsDiscount => ParentEntryIndex.HasValue;
}
