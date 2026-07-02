using floQ.Domain.Billing;
using floQ.Web.Data;
using floQ.Web.Services.Time;
using floQ.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace floQ.Web.Services.Documents;

/// <summary>
/// Implementierung des Beleg-Vertrags (<see cref="IDocumentEngine"/>) —
/// Port des batOS-DocumentEngine-Letztstands auf die floQ-Beleg-Domäne
/// (eine TPH-Tabelle, eine Entry-Tabelle, Freitext-Positionen, tenant-scoped).
///
/// Aufgeteilt als partial class (batOS-Gliederung):
///   DocumentEngine.cs           → DI, Typ-Helfer, Nummernkreis, Anlegen
///   DocumentEngine.Edit.cs      → SaveDraft, Validierung, GetDraft, Discard, Delete
///   DocumentEngine.Lifecycle.cs → Finalize, Unlock, Zahlungen
///   DocumentEngine.Read.cs      → Liste, Detail, EditorContext, offene Rechnungen
/// </summary>
public sealed partial class DocumentEngine(
    AppDbContext db,
    ITenantContext tenantContext,
    ILogger<DocumentEngine> logger) : IDocumentEngine
{
    private readonly AppDbContext _db = db;
    private readonly ITenantContext _tenantContext = tenantContext;
    private readonly ILogger<DocumentEngine> _logger = logger;

    public IQueryable<Document> DocumentsQuery => _db.Documents.AsNoTracking();

    // ══════════════════════════════════════════════════════════════════
    // Typ-Helfer
    // ══════════════════════════════════════════════════════════════════

    internal static DocumentType GetDocumentType(Document doc) => doc switch
    {
        Quote => DocumentType.Quote,
        Invoice => DocumentType.Invoice,
        CreditNote => DocumentType.CreditNote,
        CancellationInvoice => DocumentType.CancellationInvoice,
        PaymentReminder => DocumentType.PaymentReminder,
        _ => throw new InvalidOperationException($"Unbekannter Belegtyp: {doc.GetType().Name}")
    };

    /// <summary>Deutscher Typ-Name ("Angebot", "1. Mahnung", …).</summary>
    internal static string GetTypeName(Document doc) => doc switch
    {
        Quote => "Angebot",
        Invoice => "Rechnung",
        CreditNote => "Gutschrift",
        CancellationInvoice => "Stornorechnung",
        PaymentReminder pr => pr.ReminderLevel switch
        {
            0 => "Zahlungserinnerung",
            1 => "1. Mahnung",
            2 => "2. Mahnung",
            3 => "3. Mahnung",
            _ => "Mahnung"
        },
        _ => "Beleg"
    };

    // ══════════════════════════════════════════════════════════════════
    // Nummernkreis
    // ══════════════════════════════════════════════════════════════════

    public async Task<NumberResult> PeekNextNumberAsync(DocumentType type, CancellationToken ct = default)
    {
        var year = ViennaTime.Today.Year;
        var config = await _db.DocumentNumberConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.DocumentType == type && c.Year == year, ct);

        // Kein Zähler fürs laufende Jahr → Finalize legt ihn automatisch mit
        // Defaults an; die Vorschau zeigt genau diese erste Nummer.
        config ??= new DocumentNumberConfig
        {
            DocumentType = type,
            TypeCode = DocumentNumberConfig.DefaultTypeCode(type),
            Year = year
        };

        return new NumberResult(true, config.Format(config.CurrentCounter + 1), null);
    }

    /// <summary>Belegnummer ziehen, auf den Beleg schreiben und persistieren —
    /// concurrency-sicher über einen Postgres-Rowlock (SELECT … FOR UPDATE) auf die
    /// Counter-Zeile. Zeitgleiche Abschlüsse werden serialisiert; keine Nummer wird
    /// doppelt vergeben. floQ-Erweiterungen gegenüber batOS: TenantId steht im
    /// Lock-Prädikat (Raw-SQL läuft am Global Query Filter vorbei) und ein fehlender
    /// Jahres-Zähler wird automatisch mit Default-TypeCode angelegt statt zu fehlen.</summary>
    private async Task<DocumentResult> DrawAndAssignNumberAsync(Document doc, CancellationToken ct)
    {
        var type = GetDocumentType(doc);
        var year = ViennaTime.Today.Year;
        var tenantId = _tenantContext.TenantId;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var config = await LockNumberConfigAsync(tenantId, type, year, ct);
        if (config is null)
        {
            // Auto-Anlage des Jahres-Zählers. ON CONFLICT DO NOTHING deckt den
            // Race zweier zeitgleicher Erst-Abschlüsse ab — danach existiert die
            // Zeile sicher und der zweite Lock-Versuch trifft.
            var defaults = new DocumentNumberConfig();
            await _db.Database.ExecuteSqlAsync($"""
                INSERT INTO "DocumentNumberConfigs"
                    ("TenantId", "DocumentType", "TypeCode", "Year", "CurrentCounter", "Separator", "SequencePadding")
                VALUES ({tenantId}, {(int)type}, {DocumentNumberConfig.DefaultTypeCode(type)}, {year}, 0, {defaults.Separator}, {defaults.SequencePadding})
                ON CONFLICT ("TenantId", "DocumentType", "Year") DO NOTHING
                """, ct);

            config = await LockNumberConfigAsync(tenantId, type, year, ct);
            if (config is null)
                return DocumentResult.Fail($"Nummernkreis für {GetTypeName(doc)} {year} konnte nicht angelegt werden.");
        }

        config.CurrentCounter++;
        doc.Number = config.Format(config.CurrentCounter);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return DocumentResult.Ok(doc.Id);
    }

    /// <summary>Counter-Zeile exklusiv sperren — hält den Lock bis Commit und
    /// serialisiert parallele Nummernzüge desselben (Tenant, Typ, Jahr).</summary>
    private Task<DocumentNumberConfig?> LockNumberConfigAsync(
        Guid tenantId, DocumentType type, int year, CancellationToken ct)
        => _db.DocumentNumberConfigs
            .FromSql($"""
                SELECT * FROM "DocumentNumberConfigs"
                WHERE "TenantId" = {tenantId} AND "DocumentType" = {(int)type} AND "Year" = {year}
                FOR UPDATE
                """)
            .FirstOrDefaultAsync(ct);

    // ══════════════════════════════════════════════════════════════════
    // Anlegen
    // ══════════════════════════════════════════════════════════════════

    public async Task<DocumentResult> CreateDraftAsync(DocumentType type, Guid userId, CancellationToken ct = default)
    {
        // Entwürfe sind nummernlos. Die Belegnummer wird erst beim Abschluss
        // gezogen (FinalizeAsync) — so erzeugt ein gelöschter Entwurf keine Lücke
        // in der fortlaufenden Nummernfolge (§11 UStG).
        var todayUtc = ViennaTime.ToUtc(ViennaTime.Today);

        Document document = type switch
        {
            DocumentType.Quote => new Quote { ServiceDate = todayUtc },
            DocumentType.Invoice => new Invoice { ServiceDate = todayUtc },
            DocumentType.CreditNote => new CreditNote(),          // Original wird im Editor gewählt
            DocumentType.CancellationInvoice => new CancellationInvoice(), // dito
            DocumentType.PaymentReminder => new PaymentReminder
            {
                ReminderLevel = 0,
                ReminderDueDate = ViennaTime.ToUtc(ViennaTime.Today.AddDays(14))
            },
            _ => throw new InvalidOperationException($"Unbekannter Belegtyp: {type}")
        };

        document.Date = todayUtc;
        document.Status = DocumentStatus.Draft;
        document.CreatedByUserId = userId;

        _db.Documents.Add(document);
        await _db.SaveChangesAsync(ct);

        return DocumentResult.Ok(document.Id);
    }

    public async Task<DocumentResult> CreateCancellationFromInvoiceAsync(int originalInvoiceId, Guid userId, CancellationToken ct = default)
    {
        var invoice = await _db.Documents.OfType<Invoice>()
            .Include(i => i.Entries.OrderBy(e => e.SortOrder))
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == originalInvoiceId, ct);

        if (invoice is null)
            return DocumentResult.Fail("Rechnung nicht gefunden.");
        if (invoice.Status == DocumentStatus.Draft)
            return DocumentResult.Fail("Draft-Rechnungen können nicht storniert werden. Bitte Entwurf löschen.");
        if (invoice.Status == DocumentStatus.Cancelled)
            return DocumentResult.Fail("Diese Rechnung ist bereits storniert.");

        var existingCancellation = await _db.Documents.OfType<CancellationInvoice>()
            .AnyAsync(c => c.OriginalInvoiceId == originalInvoiceId, ct);
        if (existingCancellation)
            return DocumentResult.Fail("Zu dieser Rechnung existiert bereits eine Stornorechnung.");

        var doc = new CancellationInvoice
        {
            Date = ViennaTime.ToUtc(ViennaTime.Today),
            Gross = Money.Round(-invoice.Gross),
            Note = $"Storno zu Rechnung {invoice.Number}",
            Status = DocumentStatus.Draft,
            OriginalInvoiceId = invoice.Id,
            CustomerId = invoice.CustomerId,
            CreatedByUserId = userId
        };

        CopyReverseCharge(doc, invoice);
        await CopyRecipientFromOriginalAsync(doc, invoice, invoice.CustomerId, ct);

        // Vollstorno: alle Positionen mit gespiegelten (negativen) Mengen übernehmen.
        doc.Entries = invoice.Entries.Select((e, i) => new DocumentEntry
        {
            Description = e.Description,
            Quantity = -e.Quantity,
            UnitPrice = e.UnitPrice,
            VatRate = e.VatRate,
            Unit = e.Unit,
            ParentEntryIndex = e.ParentEntryIndex,
            DiscountPercent = e.DiscountPercent,
            SortOrder = i
        }).ToList();

        _db.Documents.Add(doc);
        await _db.SaveChangesAsync(ct);
        return DocumentResult.Ok(doc.Id);
    }

    public async Task<DocumentResult> CreateCreditNoteFromInvoiceAsync(int originalInvoiceId, Guid userId, CancellationToken ct = default)
    {
        var invoice = await _db.Documents.OfType<Invoice>()
            .Include(i => i.Entries.OrderBy(e => e.SortOrder))
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == originalInvoiceId, ct);

        if (invoice is null)
            return DocumentResult.Fail("Rechnung nicht gefunden.");
        if (invoice.Status == DocumentStatus.Draft)
            return DocumentResult.Fail("Zu Entwürfen kann keine Gutschrift erstellt werden.");

        var doc = new CreditNote
        {
            Date = ViennaTime.ToUtc(ViennaTime.Today),
            Note = $"Rechnung {invoice.Number}",
            Status = DocumentStatus.Draft,
            OriginalInvoiceId = invoice.Id,
            CustomerId = invoice.CustomerId,
            CreatedByUserId = userId
        };

        CopyReverseCharge(doc, invoice);
        await CopyRecipientFromOriginalAsync(doc, invoice, invoice.CustomerId, ct);

        doc.Entries = invoice.Entries.Select((e, i) => new DocumentEntry
        {
            Description = e.Description,
            Quantity = e.Quantity,
            UnitPrice = e.UnitPrice,
            VatRate = e.VatRate,
            Unit = e.Unit,
            ParentEntryIndex = e.ParentEntryIndex,
            DiscountPercent = e.DiscountPercent,
            SortOrder = i
        }).ToList();

        doc.Gross = Money.Round(CalculateGross(doc.ReverseChargeMode,
            doc.Entries.Select(e => (e.Quantity, e.UnitPrice, e.VatRate))));

        _db.Documents.Add(doc);
        await _db.SaveChangesAsync(ct);
        return DocumentResult.Ok(doc.Id);
    }

    public async Task<DocumentResult> CreateReminderFromInvoiceAsync(int sourceInvoiceId, int level, Guid userId, CancellationToken ct = default)
    {
        if (level is < 0 or > 3)
            return DocumentResult.Fail("Ungültige Mahnstufe.");

        var invoice = await _db.Documents.OfType<Invoice>()
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == sourceInvoiceId, ct);
        if (invoice is null)
            return DocumentResult.Fail("Rechnung nicht gefunden.");

        var outstanding = await CalculateOutstandingAsync(invoice, ct);
        if (outstanding <= 0)
            return DocumentResult.Fail("Auf diese Rechnung ist kein offener Betrag vorhanden.");

        var levelConfig = await _db.ReminderLevelConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Level == level, ct);
        var fee = levelConfig?.DefaultFee ?? 0m;

        var doc = new PaymentReminder
        {
            Date = ViennaTime.ToUtc(ViennaTime.Today),
            Gross = Money.Round(outstanding + fee),
            Note = $"Rechnung {invoice.Number}",
            Status = DocumentStatus.Draft,
            ReminderLevel = level,
            CustomerId = invoice.CustomerId,
            ReminderDueDate = ViennaTime.ToUtc(ViennaTime.Today.AddDays(14)),
            ReminderFee = fee,
            InterestRate = levelConfig?.DefaultInterestRate,
            InterestAmount = null,
            CreatedByUserId = userId
        };

        await CopyRecipientFromOriginalAsync(doc, invoice, invoice.CustomerId, ct);

        doc.ReminderInvoices =
        [
            new ReminderInvoice { InvoiceId = invoice.Id, OutstandingAmount = outstanding }
        ];

        _db.Documents.Add(doc);
        await _db.SaveChangesAsync(ct);
        return DocumentResult.Ok(doc.Id);
    }

    /// <summary>Weiterverarbeiten: neuen Entwurf des Zieltyps aus einer Quelle erzeugen.
    /// Verknüpfte Ziele (Gutschrift/Storno/Mahnung) laufen über die fachlichen
    /// FromInvoice-Pfade; Angebot/Rechnung sind ein freier Kopf-+Positionen-Klon.</summary>
    public async Task<DocumentResult> CreateDraftFromSourceAsync(int sourceId, DocumentType targetType, Guid userId, CancellationToken ct = default)
    {
        switch (targetType)
        {
            case DocumentType.CreditNote:
                return await CreateCreditNoteFromInvoiceAsync(sourceId, userId, ct);
            case DocumentType.CancellationInvoice:
                return await CreateCancellationFromInvoiceAsync(sourceId, userId, ct);
            case DocumentType.PaymentReminder:
                return await CreateReminderFromInvoiceAsync(sourceId, 0, userId, ct);
        }

        // Angebot / Rechnung: Kopf + Positionen aus der Quelle klonen.
        var src = await _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == sourceId, ct);
        if (src is null) return DocumentResult.Fail("Quellbeleg nicht gefunden.");

        var srcDraft = await BuildDraftDtoAsync(src, ct);

        var created = await CreateDraftAsync(targetType, userId, ct);
        if (!created.Success) return created;

        // Auf den neuen Beleg ummünzen: neuer Typ, neue (leere) Nummer,
        // Verknüpfungs-/Override-Felder fallen weg.
        srcDraft.Id = created.DocumentId;
        srcDraft.Type = targetType;
        srcDraft.Number = null;
        srcDraft.OriginalInvoiceId = null;
        srcDraft.GrossOverride = null;

        var save = await SaveDraftAsync(srcDraft, ct);
        if (!save.Success) return DocumentResult.Fail(save.Error ?? "Weiterverarbeiten fehlgeschlagen.");

        return DocumentResult.Ok(srcDraft.Id);
    }

    // ══════════════════════════════════════════════════════════════════
    // Gemeinsame Berechnungen / Snapshots
    // ══════════════════════════════════════════════════════════════════

    /// <summary>Brutto aus Positionen — bei Steuerbefreiung ohne USt-Aufschlag
    /// (Positions-Sätze bleiben gespeichert, wirken nur nicht).</summary>
    private static decimal CalculateGross(
        ReverseChargeMode mode, IEnumerable<(decimal Quantity, decimal UnitPrice, decimal VatRate)> entries)
        => mode != ReverseChargeMode.None
            ? entries.Sum(e => e.Quantity * e.UnitPrice)
            : entries.Sum(e => e.Quantity * e.UnitPrice * (1 + e.VatRate / 100m));

    /// <summary>Offener Betrag = Brutto − Gutschriften − Zahlungen (batOS-Semantik).</summary>
    private async Task<decimal> CalculateOutstandingAsync(Invoice invoice, CancellationToken ct)
    {
        var creditTotal = await _db.Documents.OfType<CreditNote>()
            .Where(cn => cn.OriginalInvoiceId == invoice.Id)
            .SumAsync(cn => (decimal?)cn.Gross, ct) ?? 0m;
        var sumPaid = await _db.Payments
            .Where(p => p.InvoiceId == invoice.Id)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        return Money.Round(invoice.Gross - creditTotal - sumPaid);
    }

    /// <summary>Folgebelege müssen die Steuerbefreiung des Originals erben —
    /// sonst weist eine Gutschrift zu einer RC-Rechnung fälschlich USt aus.</summary>
    private static void CopyReverseCharge(Document doc, Document original)
    {
        doc.ReverseChargeMode = original.ReverseChargeMode;
        doc.ReverseChargeNote = original.ReverseChargeNote;
    }

    /// <summary>Snapshot-Felder aus dem Kundenstamm befüllen („Adressbuch"-Befüllung).</summary>
    private async Task FillRecipientFromCustomerAsync(Document doc, int customerId, CancellationToken ct)
    {
        var customer = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer is null) return;
        doc.RecipientName = customer.Name;
        doc.RecipientAddress = customer.Address;
        doc.RecipientZip = customer.Zip;
        doc.RecipientCity = customer.City;
        doc.RecipientCountry = customer.CountryCode;
        doc.RecipientUid = customer.VatId;
        doc.RecipientEmail = customer.Email;
    }

    /// <summary>Snapshot vom Original-Beleg übernehmen (Gutschrift/Storno/Mahnung);
    /// Fallback Kundenstamm, wenn das Original noch keinen Snapshot trägt.</summary>
    private async Task CopyRecipientFromOriginalAsync(Document doc, Document original, int? customerId, CancellationToken ct)
    {
        if (original.HasRecipientSnapshot)
        {
            doc.RecipientName = original.RecipientName;
            doc.RecipientAddress = original.RecipientAddress;
            doc.RecipientZip = original.RecipientZip;
            doc.RecipientCity = original.RecipientCity;
            doc.RecipientCountry = original.RecipientCountry;
            doc.RecipientUid = original.RecipientUid;
            doc.RecipientEmail = original.RecipientEmail;
        }
        else if (customerId.HasValue)
        {
            await FillRecipientFromCustomerAsync(doc, customerId.Value, ct);
        }
    }
}
