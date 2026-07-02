using System.Text.Json;
using floQ.Web.Data;
using Microsoft.Extensions.Options;

namespace floQ.Web.AdminCenter;

/// <summary>
/// Inbound-Endpoints für das batOSAdminCenter (Minimal-API):
///
/// - POST /api/platform/sync       — datenloser Sync-Anstoß (AC → floQ),
///                                   weckt den Pull-Loop sofort.
/// - POST /api/admincenter/shutoff — Sofort-Shutoff/-Reaktivierung mit
///                                   Body {active, reason, at}.
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
            AppDbContext db,
            PlatformStateService state,
            ILogger<PlatformStateService> log,
            CancellationToken ct) =>
        {
            if (!ValidateKey(ctx, opts.Value, log)) return Results.Unauthorized();

            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ct);
            var root = doc.RootElement;
            var active = root.TryGetProperty("active", out var a) && a.ValueKind == JsonValueKind.True;
            var reason = root.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";
            var at = root.TryGetProperty("at", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";

            await state.WriteShutoffAsync(db, active, reason, at, ct);

            log.LogWarning("AdminCenter-Shutoff-Webhook: active={Active}, reason='{Reason}'", active, reason);
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
