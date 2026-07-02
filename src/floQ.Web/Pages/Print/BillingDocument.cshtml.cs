using floQ.Domain.Billing;
using floQ.Domain.Settings;
using floQ.Web.Data;
using floQ.Web.Services.Pdf;
using floQ.Web.Services.Time;
using floQ.Web.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace floQ.Web.Pages.Print;

/// <summary>
/// Print-Seite für Belege — wird von Playwright gerendert (batOS-Muster).
/// Zeigt nur das A4-Dokument; das Briefpapier fügt PdfSharp als
/// Vektor-Hintergrund ein.
///
/// White-Label: der Beleg trägt ausschließlich den Brand des floQ-Kunden —
/// Absender/Footer kommen aus dem <see cref="CompanyProfile"/> des Tenants,
/// nirgends floQ-Branding.
///
/// Zugriff nur über die Render-Middleware (Loopback + renderKey + Tenant) —
/// siehe <see cref="InternalRenderRequirement"/>.
/// </summary>
[Authorize(Policy = InternalRenderRequirement.PolicyName)]
public class BillingDocumentModel(AppDbContext db, ITenantContext tenantContext) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public Document Document { get; set; } = default!;
    public string DocumentTypeName { get; set; } = string.Empty;

    /// <summary>Effektiver Steuerbefreiungs-Hinweis, Kaskade: Beleg-Wortlaut →
    /// (nur Kleinunternehmer) CompanyProfile.TaxExemptionText → Builtin-Default je Modus.</summary>
    public string ReverseChargeNote
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Document.ReverseChargeNote))
                return Document.ReverseChargeNote;
            if (Document.ReverseChargeMode == ReverseChargeMode.SmallBusiness
                && !string.IsNullOrWhiteSpace(OwnCompany?.TaxExemptionText))
                return OwnCompany.TaxExemptionText;
            return ReverseChargeNoteDefaults.Builtin(Document.ReverseChargeMode);
        }
    }

    // Empfänger (autarker Beleg: Snapshot ist die Wahrheit)
    public string? RecipientName { get; set; }
    public string? RecipientAddress { get; set; }
    public string? RecipientZipCity { get; set; }
    public string? RecipientCountry { get; set; }
    public string? RecipientUid { get; set; }

    // Absender (White-Label: Brand des floQ-Kunden)
    public CompanyProfile? OwnCompany { get; set; }

    // Merkmale
    public string? ServicePeriodDisplay { get; set; }
    public string? ExternalReference { get; set; }

    // Texte
    public string IntroText { get; set; } = string.Empty;
    public string ClosingText { get; set; } = string.Empty;
    public string? ConditionNotes { get; set; }

    // Layout-Items
    public List<BillingLayoutItem> LayoutItems { get; set; } = [];
    public bool IsVisible(string key) => LayoutItems.Any(i => i.Key == key && i.IsVisible);
    public BillingLayoutItem? GetItem(string key) => LayoutItems.FirstOrDefault(i => i.Key == key && i.IsVisible);

    // Positionen
    public List<EntryView> Entries { get; set; } = [];
    public decimal TotalNet => Entries.Sum(e => e.Net);
    // Steuerbefreiung: USt ist hart 0 — die Positions-Sätze bleiben gespeichert,
    // werden aber nicht ausgewiesen (USt-Zeile zeigt 0 %).
    public decimal TotalVat => Document.ReverseChargeMode != ReverseChargeMode.None
        ? 0m : Entries.Sum(e => e.Net * e.VatRate / 100);
    public decimal TotalGross => TotalNet + TotalVat;

    // PaymentReminder
    public DateTime? ReminderDueDateVienna { get; set; }
    public decimal ReminderFee { get; set; }
    public decimal? InterestRate { get; set; }
    public decimal? InterestAmount { get; set; }
    public List<ReminderInvoiceView> ReminderInvoices { get; set; } = [];

    public sealed record ReminderInvoiceView(string Number, DateTime DateVienna, decimal OutstandingAmount);

    public class EntryView
    {
        public string Description { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal VatRate { get; set; }
        public string Unit { get; set; } = string.Empty;
        public int? ParentEntryIndex { get; set; }
        public decimal? DiscountPercent { get; set; }
        public decimal Net => Quantity * UnitPrice;
        public bool IsDiscount => ParentEntryIndex.HasValue;
        /// <summary>Wert der Reduzierung für die Rabattzeile: "10%" bzw. "45,44 €".</summary>
        public string DiscountValueLabel => DiscountPercent.HasValue
            ? DiscountPercent.Value.ToString("0.##") + "%"
            : Math.Abs(Net).ToString("N2") + " €";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == Id);
        if (doc is null) return NotFound();
        Document = doc;
        DocumentTypeName = TypeName(doc);

        Entries = SortEntriesWithDiscounts(await db.DocumentEntries.AsNoTracking()
            .Where(e => e.DocumentId == Id)
            .OrderBy(e => e.SortOrder)
            .Select(e => new EntryView
            {
                Description = e.Description,
                Quantity = e.Quantity,
                UnitPrice = e.UnitPrice,
                VatRate = e.VatRate,
                Unit = e.Unit,
                ParentEntryIndex = e.ParentEntryIndex,
                DiscountPercent = e.DiscountPercent
            }).ToListAsync());

        // Typ-spezifische Merkmale
        switch (doc)
        {
            case Quote q:
                ExternalReference = q.ExternalReference;
                ConditionNotes = q.ConditionNotes;
                if (q.ServicePeriodStart.HasValue && q.ServicePeriodEnd.HasValue)
                    ServicePeriodDisplay = FormatPeriod(q.ServicePeriodStart.Value, q.ServicePeriodEnd.Value);
                break;

            case Invoice inv:
                if (inv.ServicePeriodStart.HasValue && inv.ServicePeriodEnd.HasValue)
                    ServicePeriodDisplay = FormatPeriod(inv.ServicePeriodStart.Value, inv.ServicePeriodEnd.Value);
                break;

            case CancellationInvoice ci:
                var ciOriginal = await db.Documents.OfType<Invoice>().AsNoTracking()
                    .FirstOrDefaultAsync(i => i.Id == ci.OriginalInvoiceId);
                if (ciOriginal is not null)
                    ExternalReference = $"Storno zu Rechnung {ciOriginal.Number} vom {ViennaTime.ToVienna(ciOriginal.Date):dd.MM.yyyy}";
                break;

            case CreditNote cn:
                var cnOriginal = await db.Documents.OfType<Invoice>().AsNoTracking()
                    .FirstOrDefaultAsync(i => i.Id == cn.OriginalInvoiceId);
                if (cnOriginal is not null)
                    ExternalReference = $"Gutschrift zu Rechnung {cnOriginal.Number} vom {ViennaTime.ToVienna(cnOriginal.Date):dd.MM.yyyy}";
                break;

            case PaymentReminder pr:
                ReminderDueDateVienna = ViennaTime.ToVienna(pr.ReminderDueDate);
                ReminderFee = pr.ReminderFee;
                InterestRate = pr.InterestRate;
                InterestAmount = pr.InterestAmount;
                ReminderInvoices = await db.ReminderInvoices.AsNoTracking()
                    .Where(ri => ri.PaymentReminderId == Id)
                    .Select(ri => new ReminderInvoiceView(
                        ri.Invoice.Number, ri.Invoice.Date, ri.OutstandingAmount))
                    .ToListAsync();
                ReminderInvoices = ReminderInvoices
                    .Select(ri => ri with { DateVienna = ViennaTime.ToVienna(ri.DateVienna) })
                    .ToList();
                break;
        }

        // Texte: Mahnung aus der Stufen-Config, sonst BillingText je Belegtyp.
        if (doc is PaymentReminder prDoc)
        {
            var levelConfig = await db.ReminderLevelConfigs.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Level == prDoc.ReminderLevel);
            IntroText = levelConfig?.IntroText ?? "";
            ClosingText = levelConfig?.ClosingText ?? "";
        }
        else
        {
            var docType = DocType(doc);
            var billingText = await db.BillingTexts.AsNoTracking()
                .FirstOrDefaultAsync(t => t.DocumentType == docType);
            IntroText = billingText?.IntroText ?? "";
            ClosingText = billingText?.ClosingText ?? "";
        }

        // Empfänger: Snapshot am Beleg (autark). Fallback Kundenstamm für
        // Altbelege ohne Snapshot.
        if (doc.HasRecipientSnapshot)
        {
            RecipientName = doc.RecipientName;
            RecipientAddress = doc.RecipientAddress;
            RecipientZipCity = $"{doc.RecipientZip} {doc.RecipientCity}".Trim();
            RecipientCountry = doc.RecipientCountry;
            RecipientUid = doc.RecipientUid;
        }
        else if (doc.CustomerId.HasValue)
        {
            var customer = await db.Customers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == doc.CustomerId.Value);
            if (customer is not null)
            {
                RecipientName = customer.Name;
                RecipientAddress = customer.Address;
                RecipientZipCity = $"{customer.Zip} {customer.City}".Trim();
                RecipientCountry = customer.CountryCode;
                RecipientUid = customer.VatId;
            }
        }

        // Layout-Items des Tenants; ohne persistierte Rows greifen die
        // Code-Defaults (in-memory, read-only — Settings-Editor persistiert später).
        var docTypeForLayout = DocType(doc);
        LayoutItems = await db.BillingLayoutItems.AsNoTracking()
            .Where(i => i.DocumentType == null || i.DocumentType == docTypeForLayout)
            .OrderBy(i => i.SortOrder)
            .ToListAsync();
        if (LayoutItems.Count == 0)
            LayoutItems = BillingLayoutItem.CreateDefaults(tenantContext.TenantId);

        // Absender (genau ein CompanyProfile pro Tenant)
        OwnCompany = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync();

        return Page();
    }

    private static string TypeName(Document doc) => doc switch
    {
        Quote => "Angebot",
        Invoice => "Rechnung",
        CreditNote => "Gutschrift",
        CancellationInvoice => "Stornorechnung",
        PaymentReminder pr => pr.ReminderLevel switch
        {
            0 => "Zahlungserinnerung", 1 => "1. Mahnung",
            2 => "2. Mahnung", 3 => "3. Mahnung", _ => "Mahnung"
        },
        _ => "Beleg"
    };

    private static DocumentType DocType(Document doc) => doc switch
    {
        Quote => DocumentType.Quote,
        Invoice => DocumentType.Invoice,
        CreditNote => DocumentType.CreditNote,
        CancellationInvoice => DocumentType.CancellationInvoice,
        _ => DocumentType.PaymentReminder
    };

    private static string FormatPeriod(DateTime startUtc, DateTime endUtc)
        => $"{ViennaTime.ToVienna(startUtc):dd.MM.yyyy} – {ViennaTime.ToVienna(endUtc):dd.MM.yyyy}";

    private static List<EntryView> SortEntriesWithDiscounts(List<EntryView> entries)
    {
        // ParentEntryIndex referenziert den Voll-Array-Index (Haupt- + Rabattzeilen,
        // wie beim Speichern vergeben). entries ist nach SortOrder = Voll-Array-
        // Reihenfolge sortiert, daher per Voll-Index i matchen.
        var result = new List<EntryView>();
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].IsDiscount) continue;
            result.Add(entries[i]);
            result.AddRange(entries.Where(e => e.ParentEntryIndex == i));
        }
        return result;
    }
}
