using floQ.Web.Data;
using floQ.Web.Services.Mail;
using floQ.Web.Services.Time;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace floQ.Web.Api.V1;

/// <summary>
/// Versand-Endpoints unter /api/v1: Beleg per Mail versenden (Tracking-Link
/// oder PDF-Anhang), Versand-Historie, Tenant-SMTP-Konfiguration.
/// </summary>
public static class DistributionApi
{
    private record ApiEnvelope(bool Success, object? Data, string? ErrorMessage);
    private static IResult Ok(object? data = null) => Results.Json(new ApiEnvelope(true, data, null));
    private static IResult Fail(string message, int status = 400)
        => Results.Json(new ApiEnvelope(false, null, message), statusCode: status);

    public static void MapDistributionApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization();

        // ── Versand ──────────────────────────────────────────────────────
        api.MapPost("/documents/{id:int}/send", async (BillingDistributionService distribution,
            HttpContext http, int id, [FromBody] SendDocumentRequest req, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.RecipientEmail))
                return Fail("Bitte E-Mail-Adresse angeben.");

            var requestBaseUrl = $"{http.Request.Scheme}://{http.Request.Host}";
            try
            {
                var dist = await distribution.SendAsync(id, req.RecipientEmail, requestBaseUrl,
                    req.Message, req.AttachPdf, req.SendCopyToSelf, ct);
                return Ok(new { dist.Id, dist.RecipientEmail });
            }
            catch (InvalidOperationException ex)
            {
                return Fail(ex.Message);
            }
            catch (Exception ex)
            {
                return Fail($"Versand fehlgeschlagen: {ex.Message}", 500);
            }
        });

        // ── Versand-Historie eines Belegs ────────────────────────────────
        api.MapGet("/documents/{id:int}/distributions", async (AppDbContext db, int id, CancellationToken ct) =>
        {
            var rows = await db.DocumentDistributions.AsNoTracking()
                .Where(d => d.DocumentId == id)
                .OrderByDescending(d => d.CreatedAtUtc)
                .Select(d => new
                {
                    d.Id,
                    d.RecipientEmail,
                    d.AttachPdf,
                    SentAtVienna = ViennaTime.ToVienna(d.SentAtUtc),
                    FirstOpenedAtVienna = ViennaTime.ToVienna(d.FirstOpenedAtUtc),
                    d.OpenCount,
                    FirstDownloadedAtVienna = ViennaTime.ToVienna(d.FirstDownloadedAtUtc),
                    d.DownloadCount
                })
                .ToListAsync(ct);
            return Ok(rows);
        });

        // ── SMTP-Konfiguration des Tenants ───────────────────────────────
        api.MapGet("/mail-settings", async (AppDbContext db, TenantSecretsService secrets, CancellationToken ct) =>
        {
            var settings = await db.TenantMailSettings.AsNoTracking().FirstOrDefaultAsync(ct);
            return Ok(new
            {
                Host = settings?.Host ?? "",
                Port = settings?.Port ?? 587,
                UserName = settings?.UserName ?? "",
                Sender = settings?.Sender ?? "",
                SenderDisplayName = settings?.SenderDisplayName,
                HasPassword = await secrets.ExistsAsync("SMTP", "Password", ct)
            });
        });

        api.MapPut("/mail-settings", async (AppDbContext db, TenantSecretsService secrets,
            [FromBody] MailSettingsRequest req, CancellationToken ct) =>
        {
            var settings = await db.TenantMailSettings.FirstOrDefaultAsync(ct);
            if (settings is null)
            {
                settings = new Domain.Settings.TenantMailSettings();
                db.TenantMailSettings.Add(settings);
            }
            settings.Host = req.Host.Trim();
            settings.Port = req.Port is > 0 and < 65536 ? req.Port : 587;
            settings.UserName = req.UserName.Trim();
            settings.Sender = req.Sender.Trim();
            settings.SenderDisplayName = string.IsNullOrWhiteSpace(req.SenderDisplayName) ? null : req.SenderDisplayName.Trim();
            settings.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            // Passwort nur schreiben, wenn eines mitkommt (write-only-Feld im UI).
            if (!string.IsNullOrEmpty(req.Password))
                await secrets.SetValueAsync("SMTP", "Password", req.Password, ct);

            return Ok();
        });

        // Test-Mail an die eigene Absender-Adresse — verifiziert die Konfiguration.
        api.MapPost("/mail-settings/test", async (EmailSender emailSender, AppDbContext db, CancellationToken ct) =>
        {
            var sender = await db.TenantMailSettings.AsNoTracking()
                .Select(m => m.Sender).FirstOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(sender))
                return Fail("Bitte zuerst Absender-Adresse speichern.");

            try
            {
                await emailSender.SendAsync(sender, "floQ — SMTP-Test",
                    "<p>Wenn diese Mail ankommt, ist der E-Mail-Versand korrekt eingerichtet.</p>",
                    "Wenn diese Mail ankommt, ist der E-Mail-Versand korrekt eingerichtet.",
                    ct: ct);
                return Ok(new { sentTo = sender });
            }
            catch (Exception ex)
            {
                return Fail($"Test fehlgeschlagen: {ex.Message}");
            }
        });
    }

    public sealed record SendDocumentRequest(
        string RecipientEmail, string? Message, bool AttachPdf, bool SendCopyToSelf);
    public sealed record MailSettingsRequest(
        string Host, int Port, string UserName, string Sender, string? SenderDisplayName, string? Password);
}
