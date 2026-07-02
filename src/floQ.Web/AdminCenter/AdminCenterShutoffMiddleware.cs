namespace floQ.Web.AdminCenter;

/// <summary>
/// Wartungs-Gate. Wenn das AC die floQ-Instanz stillgelegt hat, blockt
/// diese Middleware alle Requests mit HTTP 503 + Wartungsseite.
///
/// Ausgenommen (immer durchgelassen):
/// - /api/admincenter/* — Reaktivierungs-Webhook vom AC.
/// - /api/platform/*    — Sync-Anstoß vom AC.
/// - Static-Assets (css/js/img) + favicon/robots.
///
/// Reihenfolge: vor UseAuthentication, damit Logins beim Shutoff sofort
/// abgelehnt werden.
/// </summary>
public class AdminCenterShutoffMiddleware(RequestDelegate next)
{
    private static readonly string[] ExemptPrefixes =
    {
        "/api/admincenter/",
        "/api/platform/",
        "/img/",
        "/css/",
        "/js/"
    };

    public async Task InvokeAsync(HttpContext ctx, PlatformStateService state)
    {
        if (!state.ShutoffActive)
        {
            await next(ctx);
            return;
        }

        var path = ctx.Request.Path.Value ?? "";
        foreach (var prefix in ExemptPrefixes)
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                await next(ctx);
                return;
            }
        if (string.Equals(path, "/favicon.ico", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path, "/robots.txt", StringComparison.OrdinalIgnoreCase))
        {
            await next(ctx);
            return;
        }

        ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        ctx.Response.Headers.RetryAfter = "3600";
        ctx.Response.ContentType = "text/html; charset=utf-8";

        var reason = System.Net.WebUtility.HtmlEncode(state.ShutoffReason);
        var since = System.Net.WebUtility.HtmlEncode(state.ShutoffAt);

        await ctx.Response.WriteAsync($@"<!DOCTYPE html>
<html lang=""de""><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>Vorübergehend nicht verfügbar</title>
<style>
body {{ font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', system-ui, sans-serif; background:#FAF7F2; color:#1A1A1A; margin:0; display:grid; place-items:center; min-height:100vh; padding:2rem; }}
main {{ max-width: 520px; text-align: center; }}
.wordmark {{ font-size:2rem; font-weight:800; letter-spacing:-0.045em; margin-bottom:1.5rem; }}
.wordmark .q {{ color:#B08D57; }}
h1 {{ font-size: 1.4rem; margin: 0 0 1rem; }}
p {{ color: #6b6b6b; line-height: 1.6; }}
.reason {{ background:#fff; border:1px solid #e0d9cc; border-radius:8px; padding:1rem 1.25rem; margin-top:1.5rem; text-align:left; }}
.reason-label {{ font-size:0.8rem; color:#999; text-transform:uppercase; letter-spacing:0.05em; margin-bottom:0.3rem; }}
.footer {{ font-size:0.8rem; color:#999; margin-top:2rem; }}
</style></head>
<body><main>
  <div class=""wordmark"">flo<span class=""q"">Q</span></div>
  <h1>Vorübergehend nicht verfügbar.</h1>
  <p>Bitte versuchen Sie es später erneut.</p>
  {(string.IsNullOrEmpty(reason) ? "" : $@"<div class=""reason""><div class=""reason-label"">Grund</div><div>{reason}</div></div>")}
  <div class=""footer"">Status seit: {since} UTC</div>
</main></body></html>");
    }
}

public static class AdminCenterShutoffMiddlewareExtensions
{
    public static IApplicationBuilder UseAdminCenterShutoff(this IApplicationBuilder app)
        => app.UseMiddleware<AdminCenterShutoffMiddleware>();
}
