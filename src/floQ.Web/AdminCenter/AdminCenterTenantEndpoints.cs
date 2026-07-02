using System.Text.Json;
using Microsoft.Extensions.Options;

namespace floQ.Web.AdminCenter;

/// <summary>
/// Mandanten-Lebenszyklus-Endpoints (AC → floQ). floQ hat keinen
/// Admin-Zugang — diese Aktionen löst ausschließlich das batOSAdminCenter
/// aus (Auth: X-Platform-Key zeitkonstant, Tenant via X-Instance-ShortName,
/// gleiche Header wie billing-payer):
///
/// - POST /api/admincenter/tenant-delete     — Mandant endgültig löschen
///   (Kündigung/DSGVO): alle Daten, User, Passkeys, Uploads, Caches.
/// - POST /api/admincenter/tenant-user-email — Support-Rettung: neue
///   Login-Mail für den (einzigen) User des Mandanten setzen, optional
///   Passkeys widerrufen → Kunde meldet sich per E-Mail-Code an und
///   registriert einen neuen Passkey.
/// </summary>
public static class AdminCenterTenantEndpoints
{
    public static void MapAdminCenterTenantEndpoints(this WebApplication app)
    {
        app.MapPost("/api/admincenter/tenant-delete", async (
            HttpContext ctx,
            IOptions<AdminCenterOptions> opts,
            TenantLifecycleService lifecycle,
            ILogger<TenantLifecycleService> log,
            CancellationToken ct) =>
        {
            if (!AdminCenterEndpoints.ValidateKey(ctx, opts.Value, log)) return Results.Unauthorized();

            var slug = ReadSlug(ctx);
            if (slug.Length == 0) return Results.NotFound();

            var deleted = await lifecycle.DeleteTenantAsync(slug, ct);
            return deleted
                ? Results.Ok(new { success = true, data = (object?)null, errorMessage = (string?)null })
                : Results.NotFound();
        }).AllowAnonymous();

        app.MapPost("/api/admincenter/tenant-user-email", async (
            HttpContext ctx,
            IOptions<AdminCenterOptions> opts,
            TenantLifecycleService lifecycle,
            ILogger<TenantLifecycleService> log,
            CancellationToken ct) =>
        {
            if (!AdminCenterEndpoints.ValidateKey(ctx, opts.Value, log)) return Results.Unauthorized();

            var slug = ReadSlug(ctx);
            if (slug.Length == 0) return Results.NotFound();

            string newEmail;
            var revokePasskeys = false;
            try
            {
                using var doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ct);
                newEmail = doc.RootElement.TryGetProperty("newEmail", out var e)
                    && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
                revokePasskeys = doc.RootElement.TryGetProperty("revokePasskeys", out var r)
                    && r.ValueKind == JsonValueKind.True;
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "invalid_json" });
            }
            if (string.IsNullOrWhiteSpace(newEmail))
                return Results.BadRequest(new { error = "Feld 'newEmail' fehlt." });

            var result = await lifecycle.ChangeUserEmailAsync(slug, newEmail, revokePasskeys, ct);
            return result switch
            {
                TenantLifecycleService.EmailChangeResult.Ok =>
                    Results.Ok(new { success = true, data = (object?)null, errorMessage = (string?)null }),
                TenantLifecycleService.EmailChangeResult.TenantNotFound => Results.NotFound(),
                TenantLifecycleService.EmailChangeResult.InvalidEmail =>
                    Results.BadRequest(new { error = "invalid_email" }),
                TenantLifecycleService.EmailChangeResult.EmailTaken =>
                    Results.Conflict(new { error = "email_taken" }),
                _ => Results.Conflict(new { error = "ambiguous_user" }),
            };
        }).AllowAnonymous();
    }

    /// <summary>Tenant-Slug aus X-Instance-ShortName (getrimmt + lowercase,
    /// wie beim billing-payer-Endpoint).</summary>
    private static string ReadSlug(HttpContext ctx) =>
        ctx.Request.Headers["X-Instance-ShortName"].ToString().Trim().ToLowerInvariant();
}
