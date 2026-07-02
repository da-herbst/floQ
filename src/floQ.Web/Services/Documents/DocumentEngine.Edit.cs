using floQ.Domain.Billing;
using floQ.Web.Services.Time;
using Microsoft.EntityFrameworkCore;

namespace floQ.Web.Services.Documents;

/// <summary>
/// Bearbeiten: SaveDraft (Validierung + Update), GetDraft, Discard, Delete.
/// </summary>
public sealed partial class DocumentEngine
{
    // ══════════════════════════════════════════════════════════════════
    // SaveDraft
    // ══════════════════════════════════════════════════════════════════

    public async Task<DocumentResult> SaveDraftAsync(DocumentDraftDto draft, CancellationToken ct = default)
    {
        var errors = Validate(draft);
        if (errors.Count > 0) return DocumentResult.Fail(errors);

        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == draft.Id, ct);
        if (doc is null) return DocumentResult.Fail("Beleg nicht gefunden.");
        if (doc.Status != DocumentStatus.Draft)
            return DocumentResult.Fail("Nur Entwürfe können bearbeitet werden.");
        if (GetDocumentType(doc) != draft.Type)
            return DocumentResult.Fail("Belegtyp stimmt nicht mit dem gespeicherten Beleg überein.");

        // Gemeinsame Felder. Die Belegnummer wird bewusst NICHT übernommen —
        // sie wird ausschließlich beim Abschluss gezogen.
        doc.Date = ViennaTime.ToUtc(draft.DocumentDateVienna);
        doc.Note = draft.Note;
        doc.PaymentTermDays = draft.PaymentTermDays;
        doc.PaymentTermDiscountDays = draft.PaymentTermDiscountDays;
        doc.DiscountRate = draft.DiscountRate;

        // Steuerbefreiung (Reverse Charge EU-B2B oder Drittland) → Beleg ohne USt.
        // Die Positions-USt-Sätze bleiben UNVERÄNDERT erhalten (sonst gehen sie beim
        // Zurückschalten auf „Keine" verloren); die Befreiung wirkt nur auf die
        // Brutto-Berechnung und die Anzeige/Print.
        doc.ReverseChargeMode = draft.ReverseChargeMode;
        doc.ReverseChargeNote = string.IsNullOrWhiteSpace(draft.ReverseChargeNote) ? null : draft.ReverseChargeNote.Trim();

        // Empfänger-Snapshot: die Beleg-Felder sind die Wahrheit (autarker Beleg);
        // der Kundenstamm ist nur Befüllung. Kommt vom Editor kein Name, wird aus
        // CustomerId befüllt.
        doc.RecipientName = draft.RecipientName;
        doc.RecipientAddress = draft.RecipientAddress;
        doc.RecipientZip = draft.RecipientZip;
        doc.RecipientCity = draft.RecipientCity;
        doc.RecipientCountry = draft.RecipientCountry;
        doc.RecipientUid = draft.RecipientUid;
        doc.RecipientEmail = draft.RecipientEmail;
        if (!doc.HasRecipientSnapshot && draft.CustomerId.HasValue)
            await FillRecipientFromCustomerAsync(doc, draft.CustomerId.Value, ct);

        var calculatedGross = CalculateGross(draft.ReverseChargeMode,
            draft.Entries.Select(e => (e.Quantity, e.UnitPrice, e.VatRate)));

        switch (doc)
        {
            case Quote q:
                q.CustomerId = draft.CustomerId;
                q.ServiceDate = ViennaTime.ToUtc(draft.ServiceDateVienna ?? ViennaTime.Today);
                q.ServicePeriodStart = ViennaTime.ToUtc(draft.ServicePeriodStartVienna);
                q.ServicePeriodEnd = ViennaTime.ToUtc(draft.ServicePeriodEndVienna);
                q.ValidUntil = ViennaTime.ToUtc(draft.ValidUntilVienna);
                q.ExternalReference = draft.ExternalReference;
                q.ConditionNotes = draft.ConditionNotes;
                q.Gross = Money.Round(calculatedGross);
                ReplaceEntries(doc, draft.Entries);
                break;

            case Invoice inv:
                inv.CustomerId = draft.CustomerId;
                inv.ServiceDate = ViennaTime.ToUtc(draft.ServiceDateVienna ?? ViennaTime.Today);
                inv.ServicePeriodStart = ViennaTime.ToUtc(draft.ServicePeriodStartVienna);
                inv.ServicePeriodEnd = ViennaTime.ToUtc(draft.ServicePeriodEndVienna);
                inv.Gross = Money.Round(calculatedGross);
                ReplaceEntries(doc, draft.Entries);
                break;

            case CreditNote cn:
                // Form-Wert nur übernehmen wenn gesetzt — sonst DB-Wert behalten.
                cn.OriginalInvoiceId = draft.OriginalInvoiceId ?? cn.OriginalInvoiceId;
                cn.Gross = Money.Round(draft.GrossOverride ?? calculatedGross);
                // Empfänger MUSS der der Original-Rechnung sein (buchhalterisch
                // zwingend) — immer neu aus dem Original ableiten, nie aus dem
                // Form-State.
                var cnOriginal = await _db.Documents.OfType<Invoice>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(i => i.Id == cn.OriginalInvoiceId, ct);
                if (cnOriginal is not null)
                {
                    cn.CustomerId = cnOriginal.CustomerId ?? cn.CustomerId;
                    await CopyRecipientFromOriginalAsync(cn, cnOriginal, cnOriginal.CustomerId, ct);
                }
                ReplaceEntries(doc, draft.Entries);
                break;

            case CancellationInvoice:
                // Storno ist Gegenbuchung zur Original-Rechnung — Positionen bleiben
                // gespiegelt (read-only), Gross bleibt unverändert. Editierbar sind
                // nur die gemeinsamen Kopf-Felder oben (Datum, Note, …).
                break;

            case PaymentReminder pr:
                pr.CustomerId = draft.CustomerId;
                pr.ReminderLevel = draft.ReminderLevel;
                pr.ReminderDueDate = ViennaTime.ToUtc(draft.ReminderDueDateVienna ?? ViennaTime.Today.AddDays(14));
                pr.ReminderFee = draft.ReminderFee;
                pr.InterestRate = draft.InterestRate;
                pr.InterestAmount = draft.InterestAmount;
                pr.Gross = Money.Round(
                    draft.ReminderInvoices.Sum(ri => ri.OutstandingAmount) + draft.ReminderFee + (draft.InterestAmount ?? 0));
                await ReplaceReminderInvoicesAsync(pr, draft.ReminderInvoices, ct);
                break;
        }

        await _db.SaveChangesAsync(ct);
        return DocumentResult.Ok(doc.Id);
    }

    /// <summary>Positionen ersetzen (Löschen + Neuanlage; eigene Ids, kein
    /// Key-Konflikt). SortOrder = Voll-Array-Index — derselbe Index, den
    /// <see cref="DocumentEntryDto.ParentEntryIndex"/> referenziert.</summary>
    private void ReplaceEntries(Document doc, List<DocumentEntryDto> entries)
    {
        _db.DocumentEntries.RemoveRange(_db.DocumentEntries.Where(e => e.DocumentId == doc.Id));
        foreach (var (entry, index) in entries.Select((e, i) => (e, i)))
        {
            _db.DocumentEntries.Add(new DocumentEntry
            {
                DocumentId = doc.Id,
                Description = entry.Description,
                Quantity = entry.Quantity,
                UnitPrice = entry.UnitPrice,
                VatRate = entry.VatRate,
                Unit = entry.Unit,
                ParentEntryIndex = entry.ParentEntryIndex,
                DiscountPercent = entry.DiscountPercent,
                SortOrder = index
            });
        }
    }

    /// <summary>Mahnungs-Rechnungen abgleichen. Composite-Key (ReminderId, InvoiceId):
    /// nicht Löschen+Neuanlage wie bei Entries, sondern Diff — sonst kollidieren
    /// gleiche Keys im ChangeTracker.</summary>
    private async Task ReplaceReminderInvoicesAsync(
        PaymentReminder reminder, List<ReminderInvoiceRef> target, CancellationToken ct)
    {
        var existing = await _db.ReminderInvoices
            .Where(ri => ri.PaymentReminderId == reminder.Id)
            .ToListAsync(ct);

        var targetByInvoice = target.ToDictionary(t => t.InvoiceId);

        foreach (var row in existing)
        {
            if (targetByInvoice.TryGetValue(row.InvoiceId, out var t))
                row.OutstandingAmount = t.OutstandingAmount;
            else
                _db.ReminderInvoices.Remove(row);
        }

        var existingIds = existing.Select(e => e.InvoiceId).ToHashSet();
        foreach (var t in target.Where(t => !existingIds.Contains(t.InvoiceId)))
        {
            _db.ReminderInvoices.Add(new ReminderInvoice
            {
                PaymentReminderId = reminder.Id,
                InvoiceId = t.InvoiceId,
                OutstandingAmount = t.OutstandingAmount
            });
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // Validierung
    // ══════════════════════════════════════════════════════════════════

    private static List<string> Validate(DocumentDraftDto draft)
    {
        var errors = new List<string>();

        void ValidateCommon()
        {
            // Autarker Beleg: ein manuell erfasster Empfänger (Snapshot) genügt —
            // die Kunden-Verknüpfung ist optional („Adressbuch" ist nur Befüllung).
            if (!draft.CustomerId.HasValue && string.IsNullOrWhiteSpace(draft.RecipientName))
                errors.Add("Bitte Empfänger erfassen oder Kunde auswählen.");
        }

        void ValidateEntries()
        {
            foreach (var entry in draft.Entries)
            {
                var name = string.IsNullOrWhiteSpace(entry.Description) ? "(ohne Bezeichnung)" : entry.Description;
                if (string.IsNullOrWhiteSpace(entry.Description))
                    errors.Add("Position ohne Bezeichnung.");
                if (entry.Quantity <= 0)
                    errors.Add($"Position '{name}': Menge fehlt.");
                if (!entry.IsDiscount && entry.UnitPrice == 0)
                    errors.Add($"Position '{name}': Preis fehlt.");
                if (entry.IsDiscount && entry.UnitPrice >= 0)
                    errors.Add($"Rabatt '{name}': Preis muss negativ sein.");
            }
        }

        switch (draft.Type)
        {
            case DocumentType.Quote:
            case DocumentType.Invoice:
                ValidateCommon();
                if (draft.Entries.Count == 0) errors.Add("Bitte mindestens eine Position erfassen.");
                ValidateEntries();
                break;

            case DocumentType.CreditNote:
                ValidateCommon();
                if (!draft.OriginalInvoiceId.HasValue) errors.Add("Bitte Originalrechnung auswählen.");
                if (draft.Entries.Count == 0) errors.Add("Bitte mindestens eine Position erfassen.");
                ValidateEntries();
                break;

            case DocumentType.CancellationInvoice:
                // Positionen sind gespiegelt — gespeichert werden nur Kopf-Felder.
                break;

            case DocumentType.PaymentReminder:
                ValidateCommon();
                if (draft.ReminderInvoices.Count == 0) errors.Add("Bitte mindestens eine offene Rechnung auswählen.");
                break;

            default:
                errors.Add("Unbekannter Belegtyp.");
                break;
        }

        return errors;
    }

    // ══════════════════════════════════════════════════════════════════
    // GetDraft
    // ══════════════════════════════════════════════════════════════════

    public async Task<DocumentDraftDto?> GetDraftAsync(int id, CancellationToken ct = default)
    {
        var doc = await _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null || doc.Status != DocumentStatus.Draft) return null;
        return await BuildDraftDtoAsync(doc, ct);
    }

    /// <summary>Baut das Draft-DTO (Kopf + Positionen) aus einem beliebigen Beleg —
    /// ohne Status-Guard, damit auch abgeschlossene Quellen für „Weiterverarbeiten"
    /// gelesen werden können.</summary>
    private async Task<DocumentDraftDto> BuildDraftDtoAsync(Document doc, CancellationToken ct)
    {
        var draft = new DocumentDraftDto
        {
            Id = doc.Id,
            Type = GetDocumentType(doc),
            Number = doc.Number,
            DocumentDateVienna = ViennaTime.ToVienna(doc.Date),
            CustomerId = doc.CustomerId,
            Note = doc.Note,
            PaymentTermDays = doc.PaymentTermDays,
            PaymentTermDiscountDays = doc.PaymentTermDiscountDays,
            DiscountRate = doc.DiscountRate,
            RecipientName = doc.RecipientName,
            RecipientAddress = doc.RecipientAddress,
            RecipientZip = doc.RecipientZip,
            RecipientCity = doc.RecipientCity,
            RecipientCountry = doc.RecipientCountry,
            RecipientUid = doc.RecipientUid,
            RecipientEmail = doc.RecipientEmail,
            ReverseChargeMode = doc.ReverseChargeMode,
            ReverseChargeNote = doc.ReverseChargeNote
        };

        switch (doc)
        {
            case Quote q:
                draft.ServiceDateVienna = ViennaTime.ToVienna(q.ServiceDate);
                draft.ServicePeriodStartVienna = ViennaTime.ToVienna(q.ServicePeriodStart);
                draft.ServicePeriodEndVienna = ViennaTime.ToVienna(q.ServicePeriodEnd);
                draft.ValidUntilVienna = ViennaTime.ToVienna(q.ValidUntil);
                draft.ExternalReference = q.ExternalReference;
                draft.ConditionNotes = q.ConditionNotes;
                draft.Entries = await LoadEntriesAsync(doc.Id, ct);
                break;

            case Invoice inv:
                draft.ServiceDateVienna = ViennaTime.ToVienna(inv.ServiceDate);
                draft.ServicePeriodStartVienna = ViennaTime.ToVienna(inv.ServicePeriodStart);
                draft.ServicePeriodEndVienna = ViennaTime.ToVienna(inv.ServicePeriodEnd);
                draft.Entries = await LoadEntriesAsync(doc.Id, ct);
                break;

            case CreditNote cn:
                draft.OriginalInvoiceId = cn.OriginalInvoiceId > 0 ? cn.OriginalInvoiceId : null;
                draft.GrossOverride = cn.Gross;
                draft.Entries = await LoadEntriesAsync(doc.Id, ct);
                break;

            case CancellationInvoice ci:
                draft.OriginalInvoiceId = ci.OriginalInvoiceId > 0 ? ci.OriginalInvoiceId : null;
                draft.GrossOverride = ci.Gross;
                draft.Entries = await LoadEntriesAsync(doc.Id, ct);
                break;

            case PaymentReminder pr:
                draft.ReminderLevel = pr.ReminderLevel;
                draft.ReminderDueDateVienna = ViennaTime.ToVienna(pr.ReminderDueDate);
                draft.ReminderFee = pr.ReminderFee;
                draft.InterestRate = pr.InterestRate;
                draft.InterestAmount = pr.InterestAmount;
                draft.ReminderInvoices = await _db.ReminderInvoices
                    .Where(ri => ri.PaymentReminderId == doc.Id)
                    .Select(ri => new ReminderInvoiceRef(ri.InvoiceId, ri.OutstandingAmount))
                    .ToListAsync(ct);
                break;
        }

        // Empfänger-Snapshot: LEERE Felder beim Laden aus dem aktuellen Kundenstamm
        // nachziehen (z.B. eine UID, die zum Erstellzeitpunkt noch nicht existierte).
        // Gesetzte Snapshot-Werte bleiben unangetastet — das Autark-Prinzip kippt
        // nicht; persistiert wird erst beim nächsten Speichern.
        if (draft.CustomerId is int customerId)
        {
            var customer = await _db.Customers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == customerId, ct);
            if (customer is not null)
            {
                static string? FillEmpty(string? current, string? fromCustomer) =>
                    string.IsNullOrWhiteSpace(current) ? fromCustomer : current;
                draft.RecipientName = FillEmpty(draft.RecipientName, customer.Name);
                draft.RecipientAddress = FillEmpty(draft.RecipientAddress, customer.Address);
                draft.RecipientZip = FillEmpty(draft.RecipientZip, customer.Zip);
                draft.RecipientCity = FillEmpty(draft.RecipientCity, customer.City);
                draft.RecipientCountry = FillEmpty(draft.RecipientCountry, customer.CountryCode);
                draft.RecipientUid = FillEmpty(draft.RecipientUid, customer.VatId);
                draft.RecipientEmail = FillEmpty(draft.RecipientEmail, customer.Email);
            }
        }

        return draft;
    }

    private Task<List<DocumentEntryDto>> LoadEntriesAsync(int documentId, CancellationToken ct)
        => _db.DocumentEntries.AsNoTracking()
            .Where(e => e.DocumentId == documentId)
            .OrderBy(e => e.SortOrder)
            .Select(e => new DocumentEntryDto
            {
                Description = e.Description,
                Quantity = e.Quantity,
                UnitPrice = e.UnitPrice,
                VatRate = e.VatRate,
                Unit = e.Unit,
                ParentEntryIndex = e.ParentEntryIndex,
                DiscountPercent = e.DiscountPercent
            })
            .ToListAsync(ct);

    // ══════════════════════════════════════════════════════════════════
    // Discard / Delete
    // ══════════════════════════════════════════════════════════════════

    public async Task<DocumentResult> DiscardDraftAsync(int id, CancellationToken ct = default)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return DocumentResult.Fail("Beleg nicht gefunden.");
        if (doc.Status != DocumentStatus.Draft)
            return DocumentResult.Fail("Nur Entwürfe können verworfen werden.");

        // Kinder (Entries, ReminderInvoices) hängen an Cascade-Delete-FKs.
        _db.Documents.Remove(doc);
        await _db.SaveChangesAsync(ct);
        return DocumentResult.Ok(id);
    }

    public async Task<DocumentResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return DocumentResult.Fail("Beleg nicht gefunden.");

        // Abgeschlossene Belege sind Teil der lückenlosen Nummernfolge — Löschen
        // würde ein Loch reißen. Korrektur-Pfad ist Storno bzw. Unlock.
        if (doc.Status != DocumentStatus.Draft)
            return DocumentResult.Fail("Abgeschlossene Belege können nicht gelöscht werden — bitte stornieren oder entsperren.");

        _db.Documents.Remove(doc);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Beleg {Id} gelöscht", id);
        return DocumentResult.Ok(id);
    }
}
