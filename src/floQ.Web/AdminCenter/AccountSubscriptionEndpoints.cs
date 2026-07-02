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
/// - GET  /api/v1/account/invoices          — 5050-Abo-Rechnungen des
///   Tenants (read-only aus dem AC-Archiv; Rechnungssteller ist die
///   5050 development gmbh, floQ zeigt nur an). Nur Owner.
/// - GET  /api/v1/account/invoices/{id}/pdf — Rechnungs-PDF, serverseitig
///   vom AC geproxied (der Platform-Key erreicht nie den Browser;
///   Tenant-Scoping über den Slug aus der Server-Session). Nur Owner.
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

            var slug = await GetSlugAsync(tenantContext, db, ct);
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
            if (!await IsOwnerAsync(user, tenantContext, db, ct))
                return Results.Json(
                    new { success = false, data = (object?)null, errorMessage = "Nur der Inhaber kann Module anfragen." },
                    statusCode: StatusCodes.Status403Forbidden);

            var slug = await GetSlugAsync(tenantContext, db, ct);
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

        group.MapGet("/invoices", async (
            ClaimsPrincipal user,
            ITenantContext tenantContext,
            AppDbContext db,
            AdminCenterClient ac,
            CancellationToken ct) =>
        {
            if (!tenantContext.IsResolved)
                return Results.BadRequest(new { success = false, data = (object?)null, errorMessage = "Kein aktiver Mandant." });
            if (!await IsOwnerAsync(user, tenantContext, db, ct))
                return Results.Json(
                    new { success = false, data = (object?)null, errorMessage = "Nur der Inhaber sieht die Abo-Rechnungen." },
                    statusCode: StatusCodes.Status403Forbidden);

            var slug = await GetSlugAsync(tenantContext, db, ct);
            var invoices = await ac.GetInvoicesAsync(slug, ct);
            if (invoices is null)
                return Results.Json(
                    new
                    {
                        success = false,
                        data = (object?)null,
                        errorMessage = "Das Rechnungsarchiv ist gerade nicht erreichbar — bitte später erneut versuchen.",
                    },
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            return Results.Ok(new
            {
                success = true,
                data = new
                {
                    invoices = invoices.Select(i => new
                    {
                        id = i.Id,
                        number = i.Number,
                        status = i.Status,
                        periodStart = i.PeriodStart,
                        // periodEnd kommt vom AC EXKLUSIV — die UI zeigt
                        // periodStart bis periodEnd − 1 Tag an.
                        periodEnd = i.PeriodEnd,
                        issueDate = i.IssueDate,
                        dueDate = i.DueDate,
                        netCents = i.NetCents,
                        vatRatePercent = i.VatRatePercent,
                        vatCents = i.VatCents,
                        grossCents = i.GrossCents,
                        currency = i.Currency,
                        note = i.Note,
                        pdfUrl = $"/api/v1/account/invoices/{i.Id}/pdf",
                    }),
                },
                errorMessage = (string?)null,
            });
        });

        group.MapGet("/invoices/{id:guid}/pdf", async (
            Guid id,
            HttpContext ctx,
            ClaimsPrincipal user,
            ITenantContext tenantContext,
            AppDbContext db,
            AdminCenterClient ac,
            CancellationToken ct) =>
        {
            if (!tenantContext.IsResolved) return Results.BadRequest();
            if (!await IsOwnerAsync(user, tenantContext, db, ct))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            // Tenant-Scoping: der Slug kommt IMMER aus der Server-Session —
            // das AC liefert nur Rechnungen dieser Instanz (404 sonst).
            var slug = await GetSlugAsync(tenantContext, db, ct);
            var result = await ac.GetInvoicePdfAsync(
                slug, id, ctx.Request.Headers.IfNoneMatch.ToString(), ct);

            if (result is null) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            if (result.StatusCode == StatusCodes.Status304NotModified)
                return Results.StatusCode(StatusCodes.Status304NotModified);
            if (result.StatusCode != StatusCodes.Status200OK || result.Content is null)
                return Results.NotFound();

            // PDFs sind eingefrorene Dokumente — aggressiv cachebar,
            // ETag darf zum Browser (der Platform-Key nicht).
            if (!string.IsNullOrEmpty(result.ETag))
                ctx.Response.Headers.ETag = result.ETag;
            ctx.Response.Headers.CacheControl = "private, max-age=86400, immutable";
            return Results.File(result.Content, "application/pdf", result.FileName ?? $"{id:N}.pdf");
        });
    }

    /// <summary>Owner-Check des eingeloggten Users im aktiven Tenant —
    /// Abo/Rechnungen sind Vertragsangelegenheit des Inhabers.</summary>
    private static async Task<bool> IsOwnerAsync(
        ClaimsPrincipal user, ITenantContext tenantContext, AppDbContext db, CancellationToken ct)
    {
        var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return await db.UserTenants.AnyAsync(ut =>
            ut.UserId == userId
            && ut.TenantId == tenantContext.TenantId
            && ut.Role == TenantRole.Owner, ct);
    }

    private static Task<string> GetSlugAsync(
        ITenantContext tenantContext, AppDbContext db, CancellationToken ct) =>
        db.Tenants
            .Where(t => t.Id == tenantContext.TenantId)
            .Select(t => t.Slug)
            .SingleAsync(ct);

    public sealed record ModuleRequestBody(string? Module);
}
