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
        // White-Label: streng schwarz-weiß, keine Radien/Schatten (floq-Ästhetik),
        // aber ausschließlich der Brand des Ausstellers. Nur inline-CSS (Mail-Clients).
        var bodyBlock = string.IsNullOrWhiteSpace(message)
            ? ""
            : $@"<div style=""font-size: 15px; line-height: 1.65; color: #33332f;"">{H(message).Replace("\n", "<br/>")}</div>";

        var linkBlock = includeLink
            ? $@"<a href=""{viewUrl}"" style=""display: block; background: #1a1a1a; color: #ffffff; text-align: center; height: 46px; line-height: 46px; text-decoration: none; font-weight: 600; font-size: 15px; margin: 32px 0 14px;"">Dokument ansehen</a>
      <div style=""font-size: 12.5px; color: #8a8a86; line-height: 1.6;"">
        Falls der Button nicht funktioniert:<br/>
        <a href=""{viewUrl}"" style=""color: #8a8a86; text-decoration: underline; word-break: break-all;"">{H(viewUrl)}</a>
      </div>"
            : @"<div style=""font-size: 14.5px; line-height: 1.65; color: #33332f; margin-top: 18px;"">Den Beleg finden Sie im Anhang dieser E-Mail.</div>";

        return $@"<!DOCTYPE html>
<html lang=""de""><head><meta charset=""utf-8""/></head>
<body style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', system-ui, sans-serif; background: #efefed; margin: 0; padding: 56px 20px; color: #1a1a1a;"">
  <div style=""max-width: 560px; margin: 0 auto; background: #ffffff; border: 1px solid #e0e0de;"">
    <div style=""padding: 40px 44px;"">
      {bodyBlock}
      {linkBlock}
    </div>
    <div style=""padding: 24px 44px 30px; border-top: 1px solid #e5e5e3;"">
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
            !string.IsNullOrWhiteSpace(c.Email) ? $"<a href=\"mailto:{H(c.Email)}\" style=\"color: #8a8a86; text-decoration: none;\">{H(c.Email)}</a>" : null,
            !string.IsNullOrWhiteSpace(c.VatId) ? $"UID {H(c.VatId)}" : null,
            !string.IsNullOrWhiteSpace(c.Iban) ? $"IBAN {H(c.Iban)}" : null
        }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return $@"<div style=""font-size: 13.5px; font-weight: 700; color: #111111;"">{H(c.LegalName)}</div>
      <div style=""font-size: 12.5px; color: #8a8a86; line-height: 1.7; margin-top: 4px;"">{H(addressLine)}<br/>{contactLine}</div>";
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
