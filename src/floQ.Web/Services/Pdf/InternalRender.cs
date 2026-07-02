using System.Net;
using System.Security.Claims;
using floQ.Web.Tenancy;
using Microsoft.AspNetCore.Authorization;

namespace floQ.Web.Services.Pdf;

/// <summary>
/// Auth der internen Print-Pipeline (batOS-Muster): Playwright ruft die
/// Print-Seiten per Loopback-Self-Call ohne Cookie auf. Die
/// <see cref="InternalRenderMiddleware"/> authentifiziert solche Requests
/// (Loopback + gültiger renderKey) mit einem synthetischen Principal; die
/// Policy "InternalRender" lässt genau diese durch.
/// </summary>
public class InternalRenderRequirement : IAuthorizationRequirement
{
    public const string ClaimType = "floq:InternalRender";
    public const string PolicyName = "InternalRender";
}

public class InternalRenderAuthorizationHandler : AuthorizationHandler<InternalRenderRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        InternalRenderRequirement requirement)
    {
        if (context.User.HasClaim(InternalRenderRequirement.ClaimType, "true"))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

/// <summary>
/// Authentifiziert Loopback-Requests mit gültigem <c>?renderKey=…</c>
/// (Config <c>PdfRendering:InternalSecret</c>; leer = Pipeline deaktiviert).
/// floQ-Erweiterung gegenüber batOS: der Self-Call trägt <c>?tenant={guid}</c>,
/// der als "tid"-Claim in den synthetischen Principal wandert — der nachfolgende
/// TenantResolver löst damit den Tenant auf und die tenant-scoped Queries der
/// Print-Seite greifen. Muss NACH UseAuthentication und VOR UseTenantResolver laufen.
/// </summary>
public class InternalRenderMiddleware(RequestDelegate next, IConfiguration config)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        if (ctx.User.Identity?.IsAuthenticated != true
            && ctx.Request.Query.ContainsKey("renderKey"))
        {
            var secret = config["PdfRendering:InternalSecret"];
            var renderKey = ctx.Request.Query["renderKey"].ToString();
            var remoteIp = ctx.Connection.RemoteIpAddress;

            if (!string.IsNullOrEmpty(secret) && renderKey == secret
                && remoteIp is not null && IPAddress.IsLoopback(remoteIp))
            {
                var claims = new List<Claim>
                {
                    new(InternalRenderRequirement.ClaimType, "true")
                };

                if (Guid.TryParse(ctx.Request.Query["tenant"], out var tenantId) && tenantId != Guid.Empty)
                    claims.Add(new Claim(TenantResolverMiddleware.TenantIdClaimType, tenantId.ToString()));

                ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "InternalRender"));
            }
        }

        await next(ctx);
    }
}

public static class InternalRenderMiddlewareExtensions
{
    public static IApplicationBuilder UseInternalRenderAuth(this IApplicationBuilder app)
        => app.UseMiddleware<InternalRenderMiddleware>();
}
