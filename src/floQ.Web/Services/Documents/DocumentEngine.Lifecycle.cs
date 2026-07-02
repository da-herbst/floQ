using floQ.Domain.Billing;
using floQ.Web.Services.Pdf;
using floQ.Web.Services.Time;
using Microsoft.EntityFrameworkCore;

namespace floQ.Web.Services.Documents;

/// <summary>
/// Lebenszyklus: Finalize, Unlock, PDF-Rendering/-Persistierung, Zahlungen.
/// Kaskaden-Reihenfolge beim Abschluss: Nummer ziehen → Created →
/// Storno-Kaskade → PDF persistieren (batOS-Muster).
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

        // Persistiertes Beleg-PDF erzeugen. Ein Render-Fehler kippt den Abschluss
        // NICHT mehr zurück — die Nummer ist gezogen und bleibt lückenlos; das PDF
        // lässt sich über die Vorschau/erneutes Abschließen jederzeit nachziehen.
        try
        {
            await PersistFinalizedPdfAsync(doc, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Beleg {Id}: PDF-Persistierung beim Abschluss fehlgeschlagen", id);
            return DocumentResult.Fail(
                $"Beleg wurde als {doc.Number} abgeschlossen, aber die PDF-Erzeugung ist fehlgeschlagen: {ex.Message}");
        }

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

        // Persistiertes Beleg-PDF entfernen (wird beim erneuten Abschluss neu erzeugt).
        if (!string.IsNullOrEmpty(doc.PdfPath))
        {
            try
            {
                _storage.Delete(_tenantContext.TenantId, doc.PdfPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Beleg-PDF {Path} konnte beim Entsperren nicht gelöscht werden", doc.PdfPath);
            }
        }
        doc.PdfPath = null;
        doc.Status = DocumentStatus.Draft;

        await _db.SaveChangesAsync(ct);
        return DocumentResult.Ok(id);
    }

    // ══════════════════════════════════════════════════════════════════
    // PDF
    // ══════════════════════════════════════════════════════════════════

    public async Task<PdfResult?> RenderPdfAsync(int id, bool requireFinalized, CancellationToken ct = default)
    {
        var doc = await _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return null;

        if (requireFinalized && doc.Status == DocumentStatus.Draft)
            return null;

        var pdfBytes = await RenderAsync(doc, ct);
        return new PdfResult(pdfBytes, GetPdfFileName(doc));
    }

    /// <summary>Rendert das Beleg-PDF über den Playwright-Self-Call
    /// (tenant-aware: TenantId wandert als Query-Parameter mit, siehe
    /// InternalRenderMiddleware) inkl. Briefpapier-Overlay aus dem CompanyProfile.</summary>
    private async Task<byte[]> RenderAsync(Document doc, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;

        // Briefpapier: tenant-relativer Pfad aus dem CompanyProfile.
        string? letterheadFullPath = null;
        var letterheadRelPath = await _db.CompanyProfiles
            .Select(p => p.LetterheadPdfPath)
            .FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(letterheadRelPath) && _storage.Exists(tenantId, letterheadRelPath))
            letterheadFullPath = _storage.Resolve(tenantId, letterheadRelPath);

        return await _htmlToPdf.RenderPdfAsync(
            $"/Print/BillingDocument/{doc.Id}?tenant={tenantId}",
            PdfRenderOptions.Portrait(letterheadFullPath));
    }

    /// <summary>Rendert das Beleg-PDF und legt es unter
    /// billing/{yyyy-MM}/ im Tenant-Upload-Root ab (Pfad → <see cref="Document.PdfPath"/>).</summary>
    private async Task PersistFinalizedPdfAsync(Document doc, CancellationToken ct)
    {
        var pdfBytes = await RenderAsync(doc, ct);

        var relativePath = Path.Combine("billing", ViennaTime.Now.ToString("yyyy-MM"), GetPdfFileName(doc));
        await _storage.SaveAsync(_tenantContext.TenantId, relativePath, pdfBytes, ct);

        doc.PdfPath = relativePath;
        await _db.SaveChangesAsync(ct);
    }

    private static string GetPdfFileName(Document doc)
    {
        var prefix = GetDocumentType(doc) switch
        {
            DocumentType.Quote => "AN",
            DocumentType.Invoice => "RE",
            DocumentType.CreditNote => "GS",
            DocumentType.CancellationInvoice => "SR",
            _ => "MA"
        };
        var name = string.IsNullOrWhiteSpace(doc.Number) ? $"Entwurf-{doc.Id}" : doc.Number;
        return $"{prefix}_{name}.pdf";
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
