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
<link rel=""stylesheet"" href=""/css/floq.css"">
</head>
<body>
<main class=""center-page"">
  <div class=""err-code"">503</div>
  <div class=""err-title"" style=""font-size:17px;margin-top:16px"">Vorübergehend nicht verfügbar.</div>
  <div class=""err-sub"">Bitte versuchen Sie es später wieder. <a class=""app-logout"" href=""/auth/logout"">Abmelden</a></div>
  {(string.IsNullOrEmpty(reason) ? "" : $@"<div class=""err-sub"" style=""margin-top:16px"">{reason}</div>")}
  <div class=""err-code"" style=""margin-top:24px;letter-spacing:0.08em"">Status seit {since} UTC</div>
</main></body></html>");
    }
}

public static class TenantShutoffMiddlewareExtensions
{
    public static IApplicationBuilder UseTenantShutoff(this IApplicationBuilder app)
        => app.UseMiddleware<TenantShutoffMiddleware>();
}
