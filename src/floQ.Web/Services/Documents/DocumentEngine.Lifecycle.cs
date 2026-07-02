using floQ.Domain.Billing;
using floQ.Web.Services.Time;
using Microsoft.EntityFrameworkCore;

namespace floQ.Web.Services.Documents;

/// <summary>
/// Lebenszyklus: Finalize, Unlock, Zahlungen.
/// PDF-Persistierung beim Abschluss (und Löschung beim Unlock) folgt mit der
/// PDF-Pipeline — die Kaskaden-Reihenfolge (Nummer → Created → Storno-Kaskade →
/// PDF) ist hier bereits angelegt.
/// </summary>
public sealed partial class DocumentEngine
{
    // ══════════════════════════════════════════════════════════════════
    // Finalize: Draft → Created
    // ══════════════════════════════════════════════════════════════════

    public async Task<DocumentResult> FinalizeAsync(int id, Guid userId, CancellationToken ct = default)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return DocumentResult.Fail("Beleg nicht gefunden.");

        if (doc.Status != DocumentStatus.Draft)
            return DocumentResult.Fail("Nur Entwürfe können abgeschlossen werden.");

        var errors = await ValidateForFinalizeAsync(doc, ct);
        if (errors.Count > 0)
            return DocumentResult.Fail(errors.Select(e => $"Beleg kann nicht abgeschlossen werden: {e}").ToList());

        // Belegnummer erst jetzt ziehen — vor allen weiteren Mutationen, damit der
        // eigene SaveChanges des Nummern-Zugs nur die Nummer flusht. Nur wenn noch
        // keine vergeben ist (Bestands-Entwürfe mit Nummer behalten sie).
        if (string.IsNullOrWhiteSpace(doc.Number))
        {
            var draw = await DrawAndAssignNumberAsync(doc, ct);
            if (!draw.Success) return draw;
        }

        doc.Status = DocumentStatus.Created;

        // Storno: Original-Rechnung auf Cancelled setzen (gegenstandslos).
        if (doc is CancellationInvoice ci)
        {
            var original = await _db.Documents.OfType<Invoice>()
                .FirstOrDefaultAsync(i => i.Id == ci.OriginalInvoiceId, ct);
            if (original is not null && original.Status != DocumentStatus.Cancelled)
                original.Status = DocumentStatus.Cancelled;
        }

        await _db.SaveChangesAsync(ct);

        // Ausbauschritt PDF-Pipeline: hier persistiertes Beleg-PDF erzeugen und
        // doc.PdfPath setzen (Kaskade: Nummer → Created → PDF).

        _logger.LogInformation("Beleg {Id} abgeschlossen als {Number} (User {UserId})", id, doc.Number, userId);
        return DocumentResult.Ok(id);
    }

    /// <summary>Abschluss-Validierung auf DB-Stand (nicht Form-State).</summary>
    private async Task<List<string>> ValidateForFinalizeAsync(Document doc, CancellationToken ct)
    {
        var errors = new List<string>();

        var entryCount = await _db.DocumentEntries.CountAsync(e => e.DocumentId == doc.Id, ct);
        if (entryCount == 0 && doc is not CreditNote && doc is not PaymentReminder)
            errors.Add("Keine Positionen erfasst");

        // Autarker Beleg: der Snapshot ist die Wahrheit fürs PDF — ohne Empfänger
        // kein Abschluss (Kunde allein genügt nicht mehr, der Snapshot wird beim
        // Speichern aus dem Kundenstamm befüllt).
        if (!doc.HasRecipientSnapshot)
            errors.Add("Kein Empfänger erfasst");

        // §11 Abs. 1a UStG: bei EU-Reverse-Charge ist die UID des Empfängers Pflicht.
        if (doc.ReverseChargeMode == ReverseChargeMode.EuReverseCharge
            && string.IsNullOrWhiteSpace(doc.RecipientUid))
            errors.Add("UID des Empfängers fehlt (Reverse Charge)");

        switch (doc)
        {
            case Invoice inv:
                // §11 UStG: Leistungszeitraum ist Pflicht auf der Rechnung.
                if (!inv.ServicePeriodStart.HasValue || !inv.ServicePeriodEnd.HasValue)
                    errors.Add("Leistungszeitraum fehlt");
                break;
            case CreditNote cn:
                if (cn.OriginalInvoiceId <= 0) errors.Add("Keine Originalrechnung verknüpft");
                if (cn.Gross <= 0) errors.Add("Bruttobetrag fehlt");
                break;
            case CancellationInvoice ci:
                if (ci.OriginalInvoiceId <= 0) errors.Add("Keine Originalrechnung verknüpft");
                if (ci.Gross >= 0) errors.Add("Stornobetrag muss negativ sein");
                break;
            case PaymentReminder:
                var hasInvoices = await _db.ReminderInvoices
                    .AnyAsync(ri => ri.PaymentReminderId == doc.Id, ct);
                if (!hasInvoices) errors.Add("Keine Rechnungen ausgewählt");
                break;
        }

        return errors;
    }

    // ══════════════════════════════════════════════════════════════════
    // Unlock: Created → Draft
    // ══════════════════════════════════════════════════════════════════

    public async Task<DocumentResult> UnlockAsync(int id, CancellationToken ct = default)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return DocumentResult.Fail("Beleg nicht gefunden.");

        if (doc.Status == DocumentStatus.Draft)
            return DocumentResult.Fail("Beleg ist bereits ein Entwurf.");

        // Ausbauschritt PDF-Pipeline: persistierte PDF-Datei physisch löschen.
        // Bis dahin genügt das Lösen der Verknüpfung (es existieren keine Dateien).
        doc.PdfPath = null;
        doc.Status = DocumentStatus.Draft;

        await _db.SaveChangesAsync(ct);
        return DocumentResult.Ok(id);
    }

    // ══════════════════════════════════════════════════════════════════
    // Zahlungen
    // ══════════════════════════════════════════════════════════════════

    public async Task<DocumentResult> RecordPaymentAsync(int invoiceId, decimal amount, DateTime paidDateVienna,
        PaymentMethod method, string? reference, string? note, Guid userId, CancellationToken ct = default)
    {
        if (amount <= 0m)
            return DocumentResult.Fail("Betrag muss größer als 0 sein.");

        var invoice = await _db.Documents.OfType<Invoice>()
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
        if (invoice is null)
            return DocumentResult.Fail("Rechnung nicht gefunden.");
        if (invoice.Status == DocumentStatus.Draft)
            return DocumentResult.Fail("Auf Entwürfe können keine Zahlungen erfasst werden.");
        if (invoice.Status == DocumentStatus.Cancelled)
            return DocumentResult.Fail("Stornierte Rechnungen können keine Zahlung erhalten.");

        _db.Payments.Add(new Payment
        {
            InvoiceId = invoice.Id,
            Amount = amount,
            PaidDateUtc = ViennaTime.ToUtc(paidDateVienna),
            Method = method,
            Reference = reference,
            Note = note,
            RecordedByUserId = userId
        });

        await _db.SaveChangesAsync(ct);
        return DocumentResult.Ok(invoiceId);
    }

    public async Task<DocumentResult> DeletePaymentAsync(int paymentId, CancellationToken ct = default)
    {
        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, ct);
        if (payment is null) return DocumentResult.Fail("Zahlung nicht gefunden.");

        _db.Payments.Remove(payment);
        await _db.SaveChangesAsync(ct);
        return DocumentResult.Ok(paymentId);
    }

    public async Task<List<PaymentRow>> GetPaymentsAsync(int invoiceId, CancellationToken ct = default)
    {
        var rows = await _db.Payments.AsNoTracking()
            .Where(p => p.InvoiceId == invoiceId)
            .OrderByDescending(p => p.PaidDateUtc)
            .ToListAsync(ct);

        return rows.Select(p => new PaymentRow(
            p.Id,
            ViennaTime.ToVienna(p.PaidDateUtc),
            p.Amount,
            p.Method,
            GetMethodLabel(p.Method),
            p.Reference,
            p.Note)).ToList();
    }

    private static string GetMethodLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.BankTransfer => "Überweisung",
        PaymentMethod.Cash => "Bar",
        PaymentMethod.Card => "Karte",
        PaymentMethod.DirectDebit => "SEPA-Lastschrift",
        _ => "Sonstige"
    };
}
