using floQ.Domain.Billing;
using floQ.Web.Services.Time;
using Microsoft.EntityFrameworkCore;

namespace floQ.Web.Services.Documents;

/// <summary>
/// Lesen: Beleg-Liste, Detail, Editor-Auswahllisten, offene Rechnungen,
/// Mahnstufen-Defaults.
/// </summary>
public sealed partial class DocumentEngine
{
    // ══════════════════════════════════════════════════════════════════
    // Liste
    // ══════════════════════════════════════════════════════════════════

    public async Task<List<DocumentListRow>> GetListAsync(DocumentListFilter filter, CancellationToken ct = default)
    {
        // Auto-Expire: abgelaufene offene Angebote markieren (Lazy-Pflege beim
        // Listen-Load). Abgelaufen = ValidUntil liegt vor dem heutigen Wiener Tag.
        var todayStartUtc = ViennaTime.ToUtc(ViennaTime.Today);
        var expiredQuotes = await _db.Documents.OfType<Quote>()
            .Where(q => q.SalesStatus == QuoteSalesStatus.Open
                && q.ValidUntil.HasValue
                && q.ValidUntil.Value < todayStartUtc)
            .ToListAsync(ct);

        if (expiredQuotes.Count > 0)
        {
            foreach (var q in expiredQuotes)
                q.SalesStatus = QuoteSalesStatus.Expired;
            await _db.SaveChangesAsync(ct);
        }

        var query = _db.Documents.AsNoTracking();

        // Leeres Types-Set = alle Belegtypen (Owner-only, kein Permission-Filter).
        if (filter.Types.Count > 0)
        {
            var canQuote = filter.Types.Contains(DocumentType.Quote);
            var canInvoice = filter.Types.Contains(DocumentType.Invoice);
            var canCredit = filter.Types.Contains(DocumentType.CreditNote);
            var canCancellation = filter.Types.Contains(DocumentType.CancellationInvoice);
            var canReminder = filter.Types.Contains(DocumentType.PaymentReminder);

            query = query.Where(d =>
                (canQuote && d is Quote) ||
                (canInvoice && d is Invoice) ||
                (canCredit && d is CreditNote) ||
                (canCancellation && d is CancellationInvoice) ||
                (canReminder && d is PaymentReminder));
        }

        if (filter.Id.HasValue)
            query = query.Where(d => d.Id == filter.Id.Value);

        if (filter.CustomerId.HasValue)
            query = query.Where(d => d.CustomerId == filter.CustomerId.Value);

        var documents = await query
            .OrderByDescending(d => d.Date)
            .ThenByDescending(d => d.Id)
            .ToListAsync(ct);

        var docIds = documents.Select(d => d.Id).ToList();

        var customerIds = documents.Where(d => d.CustomerId.HasValue)
            .Select(d => d.CustomerId!.Value).Distinct().ToList();
        var customerNames = await _db.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var entriesByDoc = (await _db.DocumentEntries.AsNoTracking()
            .Where(e => docIds.Contains(e.DocumentId))
            .OrderBy(e => e.SortOrder)
            .ToListAsync(ct))
            .GroupBy(e => e.DocumentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var sumPaidByInvoice = await _db.Payments.AsNoTracking()
            .Where(p => docIds.Contains(p.InvoiceId))
            .GroupBy(p => p.InvoiceId)
            .Select(g => new { InvoiceId = g.Key, Sum = g.Sum(p => p.Amount) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Sum, ct);

        var cancelledInvoiceIds = (await _db.Documents.OfType<CancellationInvoice>()
            .Where(c => docIds.Contains(c.OriginalInvoiceId))
            .Select(c => c.OriginalInvoiceId)
            .Distinct()
            .ToListAsync(ct)).ToHashSet();

        var maxReminderLevelByInvoice = await _db.ReminderInvoices.AsNoTracking()
            .Where(ri => docIds.Contains(ri.InvoiceId))
            .Select(ri => new { ri.InvoiceId, ri.PaymentReminder.ReminderLevel })
            .GroupBy(x => x.InvoiceId)
            .Select(g => new { InvoiceId = g.Key, MaxLevel = g.Max(x => x.ReminderLevel) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.MaxLevel, ct);

        return documents.Select(doc =>
        {
            var entries = entriesByDoc.TryGetValue(doc.Id, out var list) ? list : [];
            var entryDtos = entries.Select(e => new DocumentEntryDto
            {
                Description = e.Description,
                Quantity = e.Quantity,
                UnitPrice = e.UnitPrice,
                VatRate = e.VatRate,
                Unit = e.Unit,
                ParentEntryIndex = e.ParentEntryIndex,
                DiscountPercent = e.DiscountPercent
            }).ToList();

            var totalNet = entryDtos.Sum(e => e.Net);
            // Steuerbefreiung: USt ist hart 0 (wird am PDF trotzdem mit 0 % ausgewiesen).
            var totalVat = doc.ReverseChargeMode != ReverseChargeMode.None
                ? 0m
                : entryDtos.Sum(e => e.Net * e.VatRate / 100m);

            string? servicePeriod = doc switch
            {
                Invoice inv when inv.ServicePeriodStart.HasValue && inv.ServicePeriodEnd.HasValue =>
                    $"{ViennaTime.ToVienna(inv.ServicePeriodStart.Value):dd.MM.yyyy} – {ViennaTime.ToVienna(inv.ServicePeriodEnd.Value):dd.MM.yyyy}",
                Quote qt when qt.ServicePeriodStart.HasValue && qt.ServicePeriodEnd.HasValue =>
                    $"{ViennaTime.ToVienna(qt.ServicePeriodStart.Value):dd.MM.yyyy} – {ViennaTime.ToVienna(qt.ServicePeriodEnd.Value):dd.MM.yyyy}",
                _ => null
            };

            var sumPaid = doc is Invoice && sumPaidByInvoice.TryGetValue(doc.Id, out var sp) ? sp : 0m;
            var paymentState = doc is Invoice
                ? (sumPaid <= 0m ? "Open"
                    : sumPaid < doc.Gross ? "PartiallyPaid"
                    : sumPaid == doc.Gross ? "Paid"
                    : "Overpaid")
                : null;

            return new DocumentListRow
            {
                Id = doc.Id,
                Type = GetDocumentType(doc),
                Number = doc.Number,
                DateVienna = ViennaTime.ToVienna(doc.Date),
                CustomerId = doc.CustomerId,
                CustomerName = doc.CustomerId.HasValue
                    && customerNames.TryGetValue(doc.CustomerId.Value, out var cn) ? cn : "–",
                Gross = doc.Gross,
                Net = totalNet,
                Vat = totalVat,
                SumPaid = sumPaid,
                Remaining = doc.Gross - sumPaid,
                PaymentState = paymentState,
                Status = doc.Status,
                ReminderLevel = doc is PaymentReminder pr ? pr.ReminderLevel : null,
                DueDateVienna = ViennaTime.ToVienna(doc.DueDate),
                Note = doc.Note ?? string.Empty,
                PaymentTermDays = doc.PaymentTermDays,
                ServicePeriod = servicePeriod,
                ValidUntil = doc is Quote vq && vq.ValidUntil.HasValue
                    ? ViennaTime.ToVienna(vq.ValidUntil.Value).ToString("dd.MM.yyyy") : null,
                SalesStatus = doc is Quote sq ? sq.SalesStatus : null,
                HasExistingCancellation = cancelledInvoiceIds.Contains(doc.Id),
                MaxReminderLevel = maxReminderLevelByInvoice.TryGetValue(doc.Id, out var ml) ? ml : null,
                Entries = entryDtos
            };
        }).ToList();
    }

    // ══════════════════════════════════════════════════════════════════
    // Detail (Workbench-Aside)
    // ══════════════════════════════════════════════════════════════════

    public async Task<DocumentDetailDto?> GetDetailAsync(int id, CancellationToken ct = default)
    {
        var doc = await _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return null;

        string? customerName = null, customerEmail = null;
        if (doc.CustomerId.HasValue)
        {
            var customer = await _db.Customers.AsNoTracking()
                .Where(c => c.Id == doc.CustomerId.Value)
                .Select(c => new { c.Name, c.Email })
                .FirstOrDefaultAsync(ct);
            customerName = customer?.Name;
            customerEmail = customer?.Email;
        }

        int? originalInvoiceId = doc switch
        {
            CreditNote cn when cn.OriginalInvoiceId > 0 => cn.OriginalInvoiceId,
            CancellationInvoice ci when ci.OriginalInvoiceId > 0 => ci.OriginalInvoiceId,
            _ => null
        };

        string? originalNumber = null;
        if (originalInvoiceId.HasValue)
        {
            originalNumber = await _db.Documents.AsNoTracking()
                .Where(d => d.Id == originalInvoiceId.Value)
                .Select(d => d.Number)
                .FirstOrDefaultAsync(ct);
        }

        var hasExistingCancellation = doc is Invoice
            && await _db.Documents.OfType<CancellationInvoice>()
                .AnyAsync(c => c.OriginalInvoiceId == doc.Id, ct);

        int? maxReminderLevel = null;
        if (doc is Invoice)
        {
            maxReminderLevel = await _db.ReminderInvoices
                .Where(ri => ri.InvoiceId == doc.Id)
                .Select(ri => (int?)ri.PaymentReminder.ReminderLevel)
                .MaxAsync(ct);
        }

        return new DocumentDetailDto
        {
            Id = doc.Id,
            Type = GetDocumentType(doc),
            TypeName = GetTypeName(doc),
            Number = doc.Number,
            Status = doc.Status,
            DateVienna = ViennaTime.ToVienna(doc.Date),
            Gross = doc.Gross,
            CustomerId = doc.CustomerId,
            CustomerName = customerName,
            RecipientEmail = !string.IsNullOrWhiteSpace(doc.RecipientEmail) ? doc.RecipientEmail : customerEmail,
            OriginalInvoiceId = originalInvoiceId,
            OriginalInvoiceNumber = originalNumber,
            HasExistingCancellation = hasExistingCancellation,
            MaxReminderLevel = maxReminderLevel
        };
    }

    // ══════════════════════════════════════════════════════════════════
    // Editor-Auswahllisten
    // ══════════════════════════════════════════════════════════════════

    public async Task<DocumentEditorContext> GetEditorContextAsync(CancellationToken ct = default)
    {
        var customers = await _db.Customers.AsNoTracking()
            .Where(c => !c.IsArchived)
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);

        // Rechnungs-Picker (Original für Gutschrift/Storno): nur abgeschlossene.
        var invoices = await _db.Documents.OfType<Invoice>().AsNoTracking()
            .Where(i => i.Status != DocumentStatus.Draft)
            .OrderByDescending(i => i.Date)
            .Select(i => new { i.Id, i.Number, i.Date, i.Gross })
            .ToListAsync(ct);

        return new DocumentEditorContext
        {
            Customers = customers.Select(c => (c.Id, c.Name)).ToList(),
            Invoices = invoices
                .Select(i => (i.Id, $"RE {i.Number} – {ViennaTime.ToVienna(i.Date):dd.MM.yyyy} – {i.Gross:N2} €"))
                .ToList()
        };
    }

    // ══════════════════════════════════════════════════════════════════
    // Offene Rechnungen / Mahnstufen-Defaults
    // ══════════════════════════════════════════════════════════════════

    public async Task<List<OpenInvoiceDto>> GetOpenInvoicesAsync(int customerId, CancellationToken ct = default)
    {
        var invoices = await _db.Documents.OfType<Invoice>().AsNoTracking()
            .Where(i => i.CustomerId == customerId
                && i.Status != DocumentStatus.Draft
                && i.Status != DocumentStatus.Cancelled)
            .Include(i => i.Payments)
            .OrderByDescending(i => i.Date)
            .ToListAsync(ct);

        var invoiceIds = invoices.Select(i => i.Id).ToList();
        var creditTotals = await _db.Documents.OfType<CreditNote>()
            .Where(cn => invoiceIds.Contains(cn.OriginalInvoiceId))
            .GroupBy(cn => cn.OriginalInvoiceId)
            .Select(g => new { InvoiceId = g.Key, Total = g.Sum(cn => cn.Gross) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Total, ct);

        // Outstanding = Brutto − Gutschriften − Zahlungen.
        return invoices.Select(inv =>
        {
            var creditTotal = creditTotals.TryGetValue(inv.Id, out var t) ? t : 0m;
            var sumPaid = inv.Payments.Sum(p => p.Amount);
            var outstanding = Money.Round(inv.Gross - creditTotal - sumPaid);
            var dueVienna = ViennaTime.ToVienna(inv.DueDate);
            return new OpenInvoiceDto(
                inv.Id, inv.Number, ViennaTime.ToVienna(inv.Date),
                Money.Round(inv.Gross), Money.Round(sumPaid),
                outstanding, dueVienna,
                dueVienna.HasValue && dueVienna.Value.Date < ViennaTime.Today);
        }).Where(x => x.Outstanding > 0).ToList();
    }

    public async Task<ReminderLevelDefaults?> GetReminderDefaultsAsync(int level, CancellationToken ct = default)
    {
        var config = await _db.ReminderLevelConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Level == level, ct);

        return config is null
            ? null
            : new ReminderLevelDefaults(config.DefaultFee, config.DefaultInterestRate, config.IntroText, config.ClosingText);
    }
}
