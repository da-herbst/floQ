using System.Security.Claims;

namespace floQ.Web.Tenancy;

/// <summary>
/// Liest die TenantId aus dem Auth-Cookie-Claim "tid" und setzt sie im
/// scoped <see cref="ITenantContext"/>. Muss NACH UseAuthentication und
/// VOR allem laufen, was den DbContext anfasst.
/// Anonyme Requests: nichts setzen — TenantContext bleibt unresolved.
/// </summary>
public class TenantResolverMiddleware(RequestDelegate next)
{
    public const string TenantIdClaimType = "tid";

    public async Task InvokeAsync(HttpContext ctx, ITenantContext tenantContext)
    {
        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            var tidClaim = ctx.User.FindFirstValue(TenantIdClaimType);
            if (Guid.TryParse(tidClaim, out var tid) && tid != Guid.Empty)
                tenantContext.SetTenant(tid);
        }

        await next(ctx);
    }
}

public static class TenantResolverMiddlewareExtensions
{
    public static IApplicationBuilder UseTenantResolver(this IApplicationBuilder app)
        => app.UseMiddleware<TenantResolverMiddleware>();
}
