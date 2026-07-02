using floQ.Web.Data;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace floQ.Web.Services.Mail;

/// <summary>Eine einzelne Dateianlage für <see cref="EmailSender.SendAsync"/>.</summary>
public sealed record EmailAttachment(string FileName, byte[] Data, string ContentType);

/// <summary>
/// Verschickt Mails über den SMTP-Server des TENANTS (batOS-EmailSender-Muster,
/// tenant-scoped statt global): Konfiguration aus <see cref="Domain.Settings.TenantMailSettings"/>,
/// Passwort aus <see cref="TenantSecretsService"/> (Provider "SMTP", Key "Password").
/// White-Label bis in den Mail-Header — floQ tritt nie als Absender auf.
/// </summary>
public class EmailSender(
    AppDbContext db,
    TenantSecretsService secrets,
    ILogger<EmailSender> logger)
{
    /// <summary>
    /// Primär-Mail senden, optional eine eigenständige „[Kopie]"-Mail an
    /// <paramref name="copyToSelfEmail"/> in derselben SMTP-Session (best-effort —
    /// Fehler der Kopie reißen die Primär-Zustellung nicht mit).
    /// Wirft bei fehlender SMTP-Konfiguration bzw. Zustellfehler der Primär-Mail.
    /// </summary>
    public async Task SendAsync(
        string toEmail, string subject, string htmlBody, string textBody,
        string? copyToSelfEmail = null, IReadOnlyCollection<EmailAttachment>? attachments = null,
        string? replyTo = null, CancellationToken ct = default)
    {
        var settings = await db.TenantMailSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (settings is null || !settings.IsConfigured)
            throw new InvalidOperationException("SMTP ist nicht konfiguriert — bitte unter Einstellungen → E-Mail-Versand einrichten.");

        var password = await secrets.GetValueAsync("SMTP", "Password", ct);
        if (string.IsNullOrEmpty(password))
            throw new InvalidOperationException("SMTP-Passwort fehlt — bitte unter Einstellungen → E-Mail-Versand neu eintragen.");

        var displayName = settings.SenderDisplayName;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = await db.CompanyProfiles.AsNoTracking()
                .Select(p => p.LegalName)
                .FirstOrDefaultAsync(ct) ?? settings.Sender;
        }

        using var client = new MailKit.Net.Smtp.SmtpClient();
        client.AuthenticationMechanisms.Remove("NTLM");
        client.AuthenticationMechanisms.Remove("GSSAPI");
        // Port 465 = implizites SSL, sonst STARTTLS (587).
        var secureOption = settings.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await client.ConnectAsync(settings.Host, settings.Port, secureOption, ct);
        await client.AuthenticateAsync(settings.UserName, password, ct);

        var primary = BuildMessage(displayName, settings.Sender, toEmail, subject, htmlBody, textBody, attachments, replyTo);
        await client.SendAsync(primary, ct);
        logger.LogInformation("Mail an {Email} gesendet: {Subject}", toEmail, subject);

        if (!string.IsNullOrWhiteSpace(copyToSelfEmail)
            && !string.Equals(copyToSelfEmail.Trim(), toEmail.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var copy = BuildMessage(displayName, settings.Sender, copyToSelfEmail,
                    "[Kopie] " + subject, htmlBody, textBody, attachments, replyTo);
                await client.SendAsync(copy, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Kopie an {Email} fehlgeschlagen — Primär-Mail wurde zugestellt.", copyToSelfEmail);
            }
        }

        await client.DisconnectAsync(true, ct);
    }

    private static MimeMessage BuildMessage(
        string displayName, string sender, string to, string subject,
        string htmlBody, string textBody, IReadOnlyCollection<EmailAttachment>? attachments, string? replyTo)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(displayName, sender));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;

        // Reply-To senkt den Spam-Score (noreply ohne Reply-To gilt als Bulk).
        if (!string.IsNullOrWhiteSpace(replyTo))
        {
            try { message.ReplyTo.Add(MailboxAddress.Parse(replyTo)); } catch { /* invalide Adresse: ignorieren */ }
        }

        // multipart/alternative (HTML + Plain) eliminiert SpamAssassin MIME_HTML_ONLY.
        var builder = new BodyBuilder { HtmlBody = htmlBody, TextBody = textBody };
        if (attachments is not null)
        {
            foreach (var att in attachments)
                builder.Attachments.Add(att.FileName, att.Data, ContentType.Parse(att.ContentType));
        }
        message.Body = builder.ToMessageBody();
        return message;
    }
}
