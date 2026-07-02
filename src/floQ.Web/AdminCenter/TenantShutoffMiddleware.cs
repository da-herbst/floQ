using floQ.Web.Tenancy;

namespace floQ.Web.AdminCenter;

/// <summary>
/// Wartungs-Gate je Mandant. Hat das AC den Tenant des aktuellen Requests
/// stillgelegt, antwortet diese Middleware mit HTTP 503 + Wartungsseite —
/// nur für diesen Tenant, alle anderen laufen normal weiter.
///
/// Reihenfolge: NACH UseTenantResolver (braucht den aufgelösten Tenant).
/// Anonyme Requests (Landing, Login, AC-Endpoints) haben keinen Tenant und
/// passieren ungehindert — ein stillgelegter Kunde kann sich also noch
/// einloggen, sieht danach aber ausschließlich die Wartungsseite.
/// /auth/logout bleibt erreichbar, damit er die Sitzung beenden kann.
/// </summary>
public class TenantShutoffMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext ctx, ITenantContext tenantContext, TenantShutoffService shutoff)
    {
        if (!tenantContext.IsResolved)
        {
            await next(ctx);
            return;
        }

        var state = shutoff.Get(tenantContext.TenantId);
        if (state is null)
        {
            await next(ctx);
            return;
        }

        var path = ctx.Request.Path.Value ?? "";
        if (string.Equals(path, "/auth/logout", StringComparison.OrdinalIgnoreCase))
        {
            await next(ctx);
            return;
        }

        ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        ctx.Response.Headers.RetryAfter = "3600";
        ctx.Response.ContentType = "text/html; charset=utf-8";

        var reason = System.Net.WebUtility.HtmlEncode(state.Reason);
        var since = System.Net.WebUtility.HtmlEncode(state.At);

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
  <h1>Dieser Zugang ist vorübergehend nicht verfügbar.</h1>
  <p>Bitte versuchen Sie es später erneut. <a href=""/auth/logout"" style=""color:#B08D57;"">Abmelden</a></p>
  {(string.IsNullOrEmpty(reason) ? "" : $@"<div class=""reason""><div class=""reason-label"">Grund</div><div>{reason}</div></div>")}
  <div class=""footer"">Status seit: {since} UTC</div>
</main></body></html>");
    }
}

public static class TenantShutoffMiddlewareExtensions
{
    public static IApplicationBuilder UseTenantShutoff(this IApplicationBuilder app)
        => app.UseMiddleware<TenantShutoffMiddleware>();
}
