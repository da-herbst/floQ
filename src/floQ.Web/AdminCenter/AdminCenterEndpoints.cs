using System.Text.Json;
using Microsoft.Extensions.Options;

namespace floQ.Web.AdminCenter;

/// <summary>
/// Inbound-Endpoints für das batOSAdminCenter (Minimal-API):
///
/// - POST /api/platform/sync       — datenloser Sync-Anstoß (AC → floQ),
///                                   weckt den Pull-Loop sofort.
/// - POST /api/admincenter/shutoff — Shutoff-Webhook. Trägt keine Instanz-
///                                   Identität (alle Tenants teilen den
///                                   Host floq.at), darum wird er NICHT als
///                                   Zustand übernommen, sondern nur als
///                                   Anstoß behandelt: der sofortige Pull
///                                   holt den Shutoff-Zustand je Tenant aus
///                                   global-settings (Quelle der Wahrheit).
/// - GET  /health                  — Liveness für Deploy-Verify.
/// - GET  /__subscription          — Status-Ping (aktiv + Version).
///
/// Auth der POST-Endpoints: X-Platform-Key, constant-time-Vergleich.
/// </summary>
public static class AdminCenterEndpoints
{
    public static void MapAdminCenterEndpoints(this WebApplication app)
    {
        app.MapPost("/api/platform/sync", (
            HttpContext ctx,
            IOptions<AdminCenterOptions> opts,
            IAdminCenterSyncTrigger trigger,
            ILogger<AdminCenterSyncTrigger> log) =>
        {
            if (!ValidateKey(ctx, opts.Value, log)) return Results.Unauthorized();

            trigger.RequestSync();
            log.LogInformation("Platform-Sync-Push empfangen — Pull wird sofort ausgeführt.");
            return Results.Ok(new { success = true, data = (object?)null, errorMessage = (string?)null });
        }).AllowAnonymous();

        app.MapPost("/api/admincenter/shutoff", async (
            HttpContext ctx,
            IOptions<AdminCenterOptions> opts,
            IAdminCenterSyncTrigger trigger,
            ILogger<AdminCenterSyncTrigger> log,
            CancellationToken ct) =>
        {
            if (!ValidateKey(ctx, opts.Value, log)) return Results.Unauthorized();

            // Body nur fürs Log lesen — die Wahrheit holt der Pull je Tenant.
            var active = false;
            var reason = "";
            try
            {
                using var doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ct);
                active = doc.RootElement.TryGetProperty("active", out var a) && a.ValueKind == JsonValueKind.True;
                reason = doc.RootElement.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";
            }
            catch (JsonException)
            {
                // Datenlos/kaputt ist egal — der Anstoß zählt.
            }

            trigger.RequestSync();
            log.LogWarning("AdminCenter-Shutoff-Webhook (active={Active}, reason='{Reason}') — Sofort-Pull ausgelöst.",
                active, reason);
            return Results.Ok(new { success = true, data = (object?)null, errorMessage = (string?)null });
        }).AllowAnonymous();

        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
           .AllowAnonymous();

        app.MapGet("/__subscription", () => Results.Json(new
        {
            active = true,
            product = "floq",
            version = Environment.GetEnvironmentVariable("FLOQ_COMMIT_SHA") ?? "unknown",
        })).AllowAnonymous();
    }

    /// <summary>Constant-time-Vergleich des X-Platform-Key-Headers.</summary>
    private static bool ValidateKey(HttpContext ctx, AdminCenterOptions opts, ILogger log)
    {
        var expected = opts.PlatformKey;
        if (string.IsNullOrEmpty(expected))
        {
            log.LogWarning("AC-Endpoint aufgerufen, aber Instanz ist nicht ans AdminCenter angebunden.");
            return false;
        }

        var sent = ctx.Request.Headers["X-Platform-Key"].ToString();
        if (sent.Length != expected.Length) return false;
        var diff = 0;
        for (var i = 0; i < sent.Length; i++) diff |= sent[i] ^ expected[i];
        if (diff != 0)
        {
            log.LogWarning("AC-Push mit ungültigem Platform-Key abgewiesen (IP {Ip}).",
                ctx.Connection.RemoteIpAddress);
            return false;
        }
        return true;
    }
}
