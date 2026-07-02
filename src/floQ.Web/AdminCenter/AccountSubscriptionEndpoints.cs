using floQ.Domain.Tenants;
using floQ.Web.Data;
using floQ.Web.Tenancy;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace floQ.Web.AdminCenter;

/// <summary>
/// Abo-Konto-API des eingeloggten Mandanten (API-First: die Produkt-UI
/// konsumiert genau diese Endpoints, kein Sonderpfad):
///
/// - GET  /api/v1/account/subscription     — Abo-Status: alle Katalog-Module
///   mit aktiv/inaktiv (aus dem lokalen Abo-Cache) + offene Abo-Anfragen
///   (live vom AC; null wenn AC gerade nicht erreichbar).
/// - POST /api/v1/account/module-requests  — Modul anfragen (Mandant →
///   Hersteller, nur Owner). Durchgereicht ans AC, idempotent.
///
/// Zahlungsdaten und Abo-Rechnungen folgen hier, sobald der AC-seitige
/// Billing-Kontrakt steht (wird parallel im AC gebaut).
/// </summary>
public static class AccountSubscriptionEndpoints
{
    public static void MapAccountSubscriptionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/account").RequireAuthorization();

        group.MapGet("/subscription", async (
            ITenantContext tenantContext,
            AppDbContext db,
            ModuleCatalog catalog,
            ModuleGateService gate,
            AdminCenterClient ac,
            CancellationToken ct) =>
        {
            if (!tenantContext.IsResolved)
                return Results.BadRequest(new { success = false, data = (object?)null, errorMessage = "Kein aktiver Mandant." });

            var slug = await db.Tenants
                .Where(t => t.Id == tenantContext.TenantId)
                .Select(t => t.Slug)
                .SingleAsync(ct);

            var pending = await ac.GetPendingModuleRequestsAsync(slug, ct);

            var modules = catalog.All.Select(m => new
            {
                key = m.Key,
                kind = m.Kind == ModuleKind.Tool ? "tool" : "module",
                title = m.Title,
                subtitle = m.Subtitle,
                icon = m.Icon,
                iconColor = m.IconColor,
                active = gate.IsActive(tenantContext.TenantId, m.Key),
                requestPending = pending?.Any(p =>
                    string.Equals(p.Module, m.Key, StringComparison.OrdinalIgnoreCase)) ?? false,
            }).ToList();

            return Results.Ok(new
            {
                success = true,
                data = new
                {
                    modules,
                    // null = AC nicht erreichbar → UI zeigt Anfrage-Status als unbekannt.
                    pendingRequests = pending?.Select(p => new { module = p.Module, requestId = p.RequestId }),
                },
                errorMessage = (string?)null,
            });
        });

        group.MapPost("/module-requests", async (
            ModuleRequestBody body,
            ClaimsPrincipal user,
            ITenantContext tenantContext,
            AppDbContext db,
            ModuleCatalog catalog,
            AdminCenterClient ac,
            CancellationToken ct) =>
        {
            if (!tenantContext.IsResolved)
                return Results.BadRequest(new { success = false, data = (object?)null, errorMessage = "Kein aktiver Mandant." });

            var moduleKey = body.Module?.Trim().ToLowerInvariant() ?? "";
            if (moduleKey.Length == 0)
                return Results.BadRequest(new { success = false, data = (object?)null, errorMessage = "Feld 'module' fehlt." });
            if (catalog.FindByKey(moduleKey) is null)
                return Results.NotFound(new { success = false, data = (object?)null, errorMessage = "Unbekanntes Modul." });

            // Abo-Anfragen sind Vertragsangelegenheit → nur der Owner.
            var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var isOwner = await db.UserTenants.AnyAsync(ut =>
                ut.UserId == userId
                && ut.TenantId == tenantContext.TenantId
                && ut.Role == TenantRole.Owner, ct);
            if (!isOwner)
                return Results.Json(
                    new { success = false, data = (object?)null, errorMessage = "Nur der Inhaber kann Module anfragen." },
                    statusCode: StatusCodes.Status403Forbidden);

            var slug = await db.Tenants
                .Where(t => t.Id == tenantContext.TenantId)
                .Select(t => t.Slug)
                .SingleAsync(ct);

            var result = await ac.RequestModuleAsync(slug, moduleKey, ct);
            return result.Outcome switch
            {
                AdminCenterClient.ModuleRequestOutcome.Created or
                AdminCenterClient.ModuleRequestOutcome.AlreadyPending => Results.Ok(new
                {
                    success = true,
                    data = new { requestId = result.RequestId, status = "pending" },
                    errorMessage = (string?)null,
                }),
                AdminCenterClient.ModuleRequestOutcome.AlreadySubscribed => Results.Conflict(new
                {
                    success = false,
                    data = (object?)null,
                    errorMessage = "Das Modul ist bereits abonniert.",
                }),
                AdminCenterClient.ModuleRequestOutcome.UnknownModule => Results.NotFound(new
                {
                    success = false,
                    data = (object?)null,
                    errorMessage = "Unbekanntes Modul.",
                }),
                _ => Results.Json(
                    new
                    {
                        success = false,
                        data = (object?)null,
                        errorMessage = "Die Abo-Verwaltung ist gerade nicht erreichbar — bitte später erneut versuchen.",
                    },
                    statusCode: StatusCodes.Status503ServiceUnavailable),
            };
        });
    }

    public sealed record ModuleRequestBody(string? Module);
}
