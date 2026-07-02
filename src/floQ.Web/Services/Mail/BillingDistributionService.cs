using System.Net;
using System.Security.Cryptography;
using floQ.Domain.Billing;
using floQ.Web.Data;
using floQ.Web.Services.Pdf;
using floQ.Web.Services.Storage;
using floQ.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace floQ.Web.Services.Mail;

/// <summary>
/// Orchestriert den Beleg-Versand (batOS-Muster, tenant-scoped): PDF-Snapshot
/// erzeugen, Token anlegen, Mail über den Tenant-SMTP senden, Status → Sent.
/// Versandarten: Tracking-Link (Default, Landing-Page /d?t=…) oder PDF-Anhang.
/// White-Label: Mail-Hülle und Landing-Page tragen ausschließlich den Brand
/// des floQ-Kunden (CompanyProfile).
/// </summary>
public class BillingDistributionService(
    AppDbContext db,
    ITenantContext tenantContext,
    HtmlToPdfService htmlToPdf,
    UploadStorage storage,
    EmailSender emailSender,
    IConfiguration config,
    ILogger<BillingDistributionService> logger)
{
    public async Task<DocumentDistribution> SendAsync(
        int documentId, string recipientEmail, string requestBaseUrl,
        string? customMessage, bool attachPdf, bool sendCopyToSelf, CancellationToken ct = default)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct)
            ?? throw new InvalidOperationException("Beleg nicht gefunden.");
        if (doc.Status == DocumentStatus.Draft)
            throw new InvalidOperationException("Entwürfe können nicht versendet werden — bitte zuerst abschließen.");

        var profile = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct);

        // PDF-Snapshot rendern (exakt diese Datei liefert später die Landing-Page).
        var tenantId = tenantContext.TenantId;
        string? letterheadFullPath = null;
        if (!string.IsNullOrWhiteSpace(profile?.LetterheadPdfPath) && storage.Exists(tenantId, profile.LetterheadPdfPath))
            letterheadFullPath = storage.Resolve(tenantId, profile.LetterheadPdfPath);

        var pdfBytes = await htmlToPdf.RenderPdfAsync(
            $"/Print/BillingDocument/{documentId}?tenant={tenantId}",
            PdfRenderOptions.Portrait(letterheadFullPath));

        var typeName = GetTypeName(doc);
        var fileName = $"{GetTypePrefix(doc)}_{doc.Number}";
        var relativePath = $"billing/dist/{fileName}_{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";
        await storage.SaveAsync(tenantId, relativePath, pdfBytes, ct);

        // Token: 32 Zufalls-Bytes, URL-safe Base64.
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');

        var distribution = new DocumentDistribution
        {
            DocumentId = documentId,
            Token = token,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(90),
            RecipientEmail = recipientEmail.Trim(),
            AttachPdf = attachPdf,
            PdfFilePath = relativePath
        };
        db.DocumentDistributions.Add(distribution);
        await db.SaveChangesAsync(ct);

        // Basis-URL: Konfig (PublicBaseUrl) hat Vorrang vor dem Request-Wert —
        // verhindert kaputte Links bei Reverse-Proxy-Headern.
        var configured = config["PublicBaseUrl"];
        var baseUrl = (string.IsNullOrWhiteSpace(configured) ? requestBaseUrl : configured).TrimEnd('/');
        var viewUrl = $"{baseUrl}/d?t={token}";

        var subject = $"{typeName} {doc.Number} — {profile?.LegalName}".TrimEnd(' ', '—');
        var htmlBody = BuildEmailBody(profile, viewUrl, customMessage, includeLink: !attachPdf);
        var textBody = BuildEmailBodyPlain(profile, viewUrl, customMessage, includeLink: !attachPdf);

        List<EmailAttachment>? attachments = null;
        if (attachPdf)
            attachments = [new EmailAttachment($"{fileName}.pdf", pdfBytes, "application/pdf")];

        var copyTo = sendCopyToSelf ? profile?.Email : null;
        var replyTo = profile?.Email;

        await emailSender.SendAsync(distribution.RecipientEmail, subject, htmlBody, textBody,
            copyTo, attachments, replyTo, ct);

        distribution.SentAtUtc = DateTime.UtcNow;
        if (doc.Status == DocumentStatus.Created)
            doc.Status = DocumentStatus.Sent;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Beleg {Id} versendet an {Email}", documentId, distribution.RecipientEmail);
        return distribution;
    }

    internal static string GetTypeName(Document doc) => doc switch
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

    private static string GetTypePrefix(Document doc) => doc switch
    {
        Quote => "AN",
        Invoice => "RE",
        CreditNote => "GS",
        CancellationInvoice => "SR",
        _ => "MA"
    };

    // ── Mail-Hülle (batOS-Letztstand: Body = persönliche Nachricht, darunter
    //    Button/Fallback-Link bzw. Anhang-Hinweis, Firmen-Footer) ─────────────

    private static string BuildEmailBody(Domain.Settings.CompanyProfile? company, string viewUrl, string? message, bool includeLink)
    {
        // Klartext-Nachricht → HTML-encoden + Zeilenumbrüche (kein Rich-Text in V1,
        // damit keine Sanitizer-Abhängigkeit nötig ist).
        var bodyBlock = string.IsNullOrWhiteSpace(message)
            ? ""
            : $@"<div style=""font-size: 14px; line-height: 1.6; color: #1d1d1f; margin: 0 0 8px;"">{H(message).Replace("\n", "<br/>")}</div>";

        var linkBlock = includeLink
            ? $@"<div style=""margin: 24px 0 16px;"">
        <a href=""{viewUrl}"" style=""display: inline-block; background: #1d1d1f; color: #ffffff; padding: 12px 28px; border-radius: 8px; text-decoration: none; font-weight: 600; font-size: 14px;"">Dokument ansehen</a>
      </div>
      <p style=""font-size: 12px; color: #86868b; line-height: 1.6; margin: 0;"">
        Falls der Button nicht funktioniert, kopieren Sie bitte folgenden Link in Ihren Browser:<br/>
        <span style=""color: #6b7280; word-break: break-all;"">{H(viewUrl)}</span>
      </p>"
            : @"<p style=""font-size: 12px; color: #86868b; line-height: 1.6; margin: 16px 0 0;"">Den Beleg finden Sie im Anhang dieser E-Mail.</p>";

        return $@"<!DOCTYPE html>
<html lang=""de""><head><meta charset=""utf-8""/></head>
<body style=""font-family: -apple-system, system-ui, 'Segoe UI', sans-serif; background: #f5f5f7; margin: 0; padding: 40px 20px; color: #1d1d1f;"">
  <div style=""max-width: 560px; margin: 0 auto; background: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 2px 12px rgba(0,0,0,0.08);"">
    <div style=""padding: 32px;"">
      {bodyBlock}
      {linkBlock}
    </div>
    <div style=""padding: 16px 32px; background: #f9f9f9; border-top: 1px solid #e5e7eb; font-size: 11px; color: #86868b; line-height: 1.5;"">
      {BuildFooterHtml(company)}
    </div>
  </div>
</body></html>";
    }

    private static string BuildEmailBodyPlain(Domain.Settings.CompanyProfile? company, string viewUrl, string? message, bool includeLink)
    {
        var bodyBlock = string.IsNullOrWhiteSpace(message) ? "" : message.Trim() + "\n\n";
        var linkBlock = includeLink
            ? $"Dokument ansehen:\n{viewUrl}"
            : "Den Beleg finden Sie im Anhang dieser E-Mail.";
        return $"{bodyBlock}{linkBlock}\n\n--\n{BuildFooterPlain(company)}\n";
    }

    private static string BuildFooterHtml(Domain.Settings.CompanyProfile? c)
    {
        if (c is null) return "";
        var addressLine = string.Join(", ", new[] { c.Street, $"{c.ZipCode} {c.City}".Trim(), c.CountryCode }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        var contactLine = string.Join(" · ", new[]
        {
            !string.IsNullOrWhiteSpace(c.Email) ? $"<a href=\"mailto:{H(c.Email)}\" style=\"color: #6b7280;\">{H(c.Email)}</a>" : null,
            !string.IsNullOrWhiteSpace(c.VatId) ? $"UID {H(c.VatId)}" : null,
            !string.IsNullOrWhiteSpace(c.Iban) ? $"IBAN {H(c.Iban)}" : null
        }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return $@"<strong style=""color: #4b5563;"">{H(c.LegalName)}</strong><br/>{H(addressLine)}<br/>{contactLine}";
    }

    private static string BuildFooterPlain(Domain.Settings.CompanyProfile? c)
    {
        if (c is null) return "";
        var lines = new List<string> { c.LegalName };
        var addr = string.Join(", ", new[] { c.Street, $"{c.ZipCode} {c.City}".Trim(), c.CountryCode }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(addr)) lines.Add(addr);
        if (!string.IsNullOrWhiteSpace(c.Email)) lines.Add(c.Email!);
        if (!string.IsNullOrWhiteSpace(c.VatId)) lines.Add($"UID {c.VatId}");
        if (!string.IsNullOrWhiteSpace(c.Iban)) lines.Add($"IBAN {c.Iban}");
        return string.Join("\n", lines);
    }

    private static string H(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
}
