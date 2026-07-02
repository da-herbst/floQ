using floQ.Domain.Tenants;

namespace floQ.Domain.Billing;

/// <summary>
/// Druck-Layout-Element des Beleg-PDFs (batOS-Konvention): mm-Koordinaten für
/// Kopfblöcke (Empfänger, Merkmale, Titel, Vortext), Flow-Padding für die
/// Positionstabelle, Sichtbarkeits-Flags für Spalten/Summen/Footer-Teile.
/// <see cref="DocumentType"/> null = gilt für alle Belegtypen.
/// Wird pro Tenant mit <see cref="CreateDefaults"/> vorbelegt; Settings-Editor folgt.
/// </summary>
public class BillingLayoutItem : TenantScopedEntity
{
    public int Id { get; set; }

    /// <summary>Element-Schlüssel ("RecipientName", "Table", "TotalVat", …).</summary>
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Group { get; set; } = "";

    public DocumentType? DocumentType { get; set; }
    public bool IsVisible { get; set; } = true;

    // mm-Koordinaten (Kopfblöcke absolut, Tabelle als Flow-Paddings)
    public double? Top { get; set; }
    public double? Left { get; set; }
    public double? Right { get; set; }
    public double? Width { get; set; }
    /// <summary>Schriftgröße in pt.</summary>
    public double? FontSize { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Default-Layout eines neuen Tenants (batOS-Maße als Startwert).</summary>
    public static List<BillingLayoutItem> CreateDefaults(Guid tenantId)
    {
        var i = 0;
        BillingLayoutItem L(string key, string label, string group,
            double? top = null, double? left = null, double? right = null,
            double? width = null, double? fontSize = null, bool visible = true) => new()
        {
            TenantId = tenantId,
            Key = key,
            Label = label,
            Group = group,
            Top = top,
            Left = left,
            Right = right,
            Width = width,
            FontSize = fontSize,
            IsVisible = visible,
            SortOrder = ++i
        };

        return
        [
            // Empfänger-Block (Fensterkuvert-Position links)
            L("RecipientName", "Empfänger", "Empfänger", top: 50, left: 20, width: 85, fontSize: 10),
            L("RecipientAddress", "Adresse", "Empfänger"),
            L("RecipientZipCity", "PLZ/Ort", "Empfänger"),
            L("RecipientCountry", "Land", "Empfänger"),

            // Merkmal-Block (rechts)
            L("DocNumber", "Belegnummer", "Merkmale", top: 50, right: 15, width: 60, fontSize: 9),
            L("DocDate", "Datum", "Merkmale"),
            L("ServicePeriod", "Leistungszeitraum", "Merkmale"),
            L("ValidUntil", "Gültig bis", "Merkmale"),
            L("ExternalReference", "Referenz", "Merkmale"),

            // Titel + Vortext
            L("Title", "Titel", "Texte", top: 100, left: 20, fontSize: 14),
            L("IntroText", "Vortext", "Texte", top: 110, left: 20, right: 15, fontSize: 10),

            // Positionstabelle (Flow-Layout: Top/Left/Right = Paddings)
            L("Table", "Positionstabelle", "Tabelle", top: 125, left: 20, right: 15, fontSize: 10),
            L("TableColPos", "Spalte Pos.", "Tabelle"),
            L("TableColDesc", "Spalte Bezeichnung", "Tabelle"),
            L("TableColPrice", "Spalte Einzelpreis", "Tabelle"),
            L("TableColQty", "Spalte Menge", "Tabelle"),
            L("TableColTotal", "Spalte Netto", "Tabelle"),

            // Summen
            L("TotalNet", "Summe netto", "Summen"),
            L("TotalVat", "Umsatzsteuer", "Summen"),
            L("TotalGross", "Summe brutto", "Summen"),

            // Abschluss-Texte
            L("Note", "Notiz", "Texte"),
            L("PaymentTerms", "Zahlungsbedingungen", "Texte"),
            L("ClosingText", "Endtext", "Texte"),

            // Footer (Chromium-FooterTemplate, jede Seite)
            L("FooterCompany", "Footer Firma", "Footer", fontSize: 7.5),
            L("FooterUid", "Footer UID", "Footer"),
            L("FooterContact", "Footer Kontakt", "Footer"),
            L("FooterBank", "Footer Bank", "Footer")
        ];
    }
}
