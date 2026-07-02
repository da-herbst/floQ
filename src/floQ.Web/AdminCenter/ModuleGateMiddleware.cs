namespace floQ.Web.AdminCenter;

/// <summary>
/// Serverseitiges Modul-Gating (batOS-Konvention): Requests unterhalb des
/// <see cref="ModuleDescriptor.RoutePrefix"/> eines nicht abonnierten Moduls
/// werden mit 503 beantwortet. Grundlage ist ausschließlich der lokale
/// Abo-Cache (<see cref="ModuleGateService"/>) — nie ein Live-Call ans AC.
///
/// Ergänzend blendet die Navigation nicht abonnierte Module aus (Schnittmenge
/// Katalog ∩ Abo über <see cref="ModuleGateService.ActiveKeys"/>) — dieses
/// Gate ist die serverseitige Verteidigung dahinter.
/// </summary>
public class ModuleGateMiddleware(
    RequestDelegate next,
    ModuleCatalog catalog,
    ModuleGateService gate)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? "";

        foreach (var module in catalog.All)
        {
            if (!path.StartsWith(module.RoutePrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (gate.IsActive(module.Key))
                break;

            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;

            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                await ctx.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    data = (object?)null,
                    errorMessage = $"Modul '{module.Title}' ist nicht abonniert.",
                });
            }
            else
            {
                ctx.Response.ContentType = "text/plain; charset=utf-8";
                await ctx.Response.WriteAsync($"Modul '{module.Title}' ist nicht abonniert.");
            }
            return;
        }

        await next(ctx);
    }
}

public static class ModuleGateMiddlewareExtensions
{
    public static IApplicationBuilder UseModuleGate(this IApplicationBuilder app)
        => app.UseMiddleware<ModuleGateMiddleware>();
}
