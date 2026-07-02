using System.Security.Claims;
using floQ.Domain.Billing;
using floQ.Web.Services.Documents;
using Microsoft.AspNetCore.Mvc;

namespace floQ.Web.Api.V1;

/// <summary>
/// REST-API der Beleg-Domäne unter /api/v1 (API-First: jeder UI-Flow läuft
/// hierüber, die Razor Pages sind reine Consumer). Antwortformat einheitlich
/// <c>{ success, data, errorMessage }</c>.
///
/// CSRF: Cookie ist SameSite=Lax — Cross-Site-POSTs senden das Auth-Cookie
/// nicht mit; alle mutierenden Endpoints sind POST/PUT/DELETE.
/// </summary>
public static class BillingApi
{
    private record ApiEnvelope(bool Success, object? Data, string? ErrorMessage);
    private static IResult Ok(object? data = null) => Results.Json(new ApiEnvelope(true, data, null));
    private static IResult Fail(string message, int status = 400)
        => Results.Json(new ApiEnvelope(false, null, message), statusCode: status);

    private static Guid UserId(ClaimsPrincipal user)
        => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static void MapBillingApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization();

        // ── Liste / Detail / Draft ───────────────────────────────────────
        api.MapGet("/documents", async (IDocumentEngine engine, int? type, int? customerId, CancellationToken ct) =>
        {
            var types = new HashSet<DocumentType>();
            if (type.HasValue) types.Add((DocumentType)type.Value);
            var rows = await engine.GetListAsync(new DocumentListFilter { Types = types, CustomerId = customerId }, ct);
            return Ok(rows);
        });

        api.MapGet("/documents/{id:int}", async (IDocumentEngine engine, int id, CancellationToken ct) =>
        {
            var detail = await engine.GetDetailAsync(id, ct);
            return detail is null ? Fail("Beleg nicht gefunden.", 404) : Ok(detail);
        });

        api.MapGet("/documents/{id:int}/draft", async (IDocumentEngine engine, int id, CancellationToken ct) =>
        {
            var draft = await engine.GetDraftAsync(id, ct);
            return draft is null ? Fail("Entwurf nicht gefunden (Beleg abgeschlossen?).", 404) : Ok(draft);
        });

        // ── Anlegen / Speichern ──────────────────────────────────────────
        api.MapPost("/documents", async (IDocumentEngine engine, ClaimsPrincipal user,
            [FromBody] CreateDocumentRequest req, CancellationToken ct) =>
        {
            var result = await engine.CreateDraftAsync(req.Type, UserId(user), ct);
            return result.Success ? Ok(new { id = result.DocumentId }) : Fail(result.Error!);
        });

        api.MapPut("/documents/{id:int}/draft", async (IDocumentEngine engine, int id,
            [FromBody] DocumentDraftDto draft, CancellationToken ct) =>
        {
            draft.Id = id;
            var result = await engine.SaveDraftAsync(draft, ct);
            return result.Success ? Ok(new { id = result.DocumentId }) : Fail(result.Error!);
        });

        // Weiterverarbeiten / Folgebelege (Storno, Gutschrift, Mahnung, Klon).
        api.MapPost("/documents/{id:int}/process", async (IDocumentEngine engine, ClaimsPrincipal user,
            int id, [FromBody] ProcessDocumentRequest req, CancellationToken ct) =>
        {
            var result = req.TargetType == DocumentType.PaymentReminder
                ? await engine.CreateReminderFromInvoiceAsync(id, req.ReminderLevel ?? 0, UserId(user), ct)
                : await engine.CreateDraftFromSourceAsync(id, req.TargetType, UserId(user), ct);
            return result.Success ? Ok(new { id = result.DocumentId }) : Fail(result.Error!);
        });

        // ── Lebenszyklus ─────────────────────────────────────────────────
        api.MapPost("/documents/{id:int}/finalize", async (IDocumentEngine engine, ClaimsPrincipal user,
            int id, CancellationToken ct) =>
        {
            var result = await engine.FinalizeAsync(id, UserId(user), ct);
            return result.Success ? Ok(new { id = result.DocumentId }) : Fail(result.Error!);
        });

        api.MapPost("/documents/{id:int}/unlock", async (IDocumentEngine engine, int id, CancellationToken ct) =>
        {
            var result = await engine.UnlockAsync(id, ct);
            return result.Success ? Ok(new { id = result.DocumentId }) : Fail(result.Error!);
        });

        api.MapDelete("/documents/{id:int}", async (IDocumentEngine engine, int id, CancellationToken ct) =>
        {
            var result = await engine.DeleteAsync(id, ct);
            return result.Success ? Ok(new { id = result.DocumentId }) : Fail(result.Error!);
        });

        // ── PDF (Vorschau inline, Download als Attachment) ───────────────
        api.MapGet("/documents/{id:int}/pdf", async (IDocumentEngine engine, int id,
            bool? download, CancellationToken ct) =>
        {
            var isDownload = download == true;
            var pdf = await engine.RenderPdfAsync(id, requireFinalized: isDownload, ct);
            if (pdf is null) return Fail("Beleg nicht gefunden oder noch nicht abgeschlossen.", 404);
            return isDownload
                ? Results.File(pdf.Bytes, "application/pdf", pdf.FileName)
                : Results.File(pdf.Bytes, "application/pdf");
        });

        // ── Nummernkreis / Auswahllisten / Mahnwesen ─────────────────────
        api.MapGet("/documents/peek-number/{type:int}", async (IDocumentEngine engine, int type, CancellationToken ct) =>
        {
            var result = await engine.PeekNextNumberAsync((DocumentType)type, ct);
            return result.Success ? Ok(new { number = result.Number }) : Fail(result.Error!);
        });

        api.MapGet("/editor-context", async (IDocumentEngine engine, CancellationToken ct) =>
        {
            var ctx = await engine.GetEditorContextAsync(ct);
            return Ok(new
            {
                customers = ctx.Customers.Select(c => new { id = c.Id, label = c.Label }),
                invoices = ctx.Invoices.Select(i => new { id = i.Id, label = i.Label })
            });
        });

        api.MapGet("/open-invoices/{customerId:int}", async (IDocumentEngine engine, int customerId, CancellationToken ct)
            => Ok(await engine.GetOpenInvoicesAsync(customerId, ct)));

        api.MapGet("/reminder-defaults/{level:int}", async (IDocumentEngine engine, int level, CancellationToken ct)
            => Ok(await engine.GetReminderDefaultsAsync(level, ct)));

        // ── Zahlungen ────────────────────────────────────────────────────
        api.MapGet("/documents/{id:int}/payments", async (IDocumentEngine engine, int id, CancellationToken ct)
            => Ok(await engine.GetPaymentsAsync(id, ct)));

        api.MapPost("/documents/{id:int}/payments", async (IDocumentEngine engine, ClaimsPrincipal user,
            int id, [FromBody] RecordPaymentRequest req, CancellationToken ct) =>
        {
            var result = await engine.RecordPaymentAsync(id, req.Amount, req.PaidDate,
                req.Method, req.Reference, req.Note, UserId(user), ct);
            return result.Success ? Ok(new { id = result.DocumentId }) : Fail(result.Error!);
        });

        api.MapDelete("/payments/{paymentId:int}", async (IDocumentEngine engine, int paymentId, CancellationToken ct) =>
        {
            var result = await engine.DeletePaymentAsync(paymentId, ct);
            return result.Success ? Ok() : Fail(result.Error!);
        });
    }

    public sealed record CreateDocumentRequest(DocumentType Type);
    public sealed record ProcessDocumentRequest(DocumentType TargetType, int? ReminderLevel);
    public sealed record RecordPaymentRequest(
        decimal Amount, DateTime PaidDate, PaymentMethod Method, string? Reference, string? Note);
}
