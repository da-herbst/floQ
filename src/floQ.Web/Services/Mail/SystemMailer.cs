using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace floQ.Web.Services.Mail;

/// <summary>
/// Zentraler floQ-Absender für System-Mails (Login-Codes, später
/// Plattform-Benachrichtigungen). Bewusst getrennt vom
/// <see cref="EmailSender"/>: der verschickt White-Label über den SMTP des
/// TENANTS — System-Mails kommen von floQ selbst und brauchen einen
/// eigenen, per Deploy konfigurierten Account.
/// </summary>
public class SystemMailOptions
{
    public const string SectionName = "SystemMail";

    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string UserName { get; set; } = "";
    /// <summary>Kommt per ENV/Deploy-Secret (SystemMail__Password), nie aus appsettings.</summary>
    public string Password { get; set; } = "";
    public string Sender { get; set; } = "";
    public string SenderDisplayName { get; set; } = "floQ";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host)
        && !string.IsNullOrWhiteSpace(Sender);
}

public class SystemMailer(
    IOptions<SystemMailOptions> options,
    ILogger<SystemMailer> log)
{
    private readonly SystemMailOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    /// <summary>Wirft bei fehlender Konfiguration bzw. Zustellfehler —
    /// der Aufrufer entscheidet, was das für seinen Flow bedeutet.</summary>
    public async Task SendAsync(
        string toEmail, string subject, string htmlBody, string textBody, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("System-SMTP ist nicht konfiguriert (Section 'SystemMail').");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.SenderDisplayName, _options.Sender));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody, TextBody = textBody }.ToMessageBody();

        using var client = new MailKit.Net.Smtp.SmtpClient();
        client.AuthenticationMechanisms.Remove("NTLM");
        client.AuthenticationMechanisms.Remove("GSSAPI");
        // Port 465 = implizites SSL, sonst STARTTLS (587) — wie EmailSender.
        var secureOption = _options.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await client.ConnectAsync(_options.Host, _options.Port, secureOption, ct);
        if (!string.IsNullOrEmpty(_options.UserName))
            await client.AuthenticateAsync(_options.UserName, _options.Password, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);

        log.LogInformation("System-Mail an {Email} gesendet: {Subject}", toEmail, subject);
    }
}
