using floQ.Web.Data;
using floQ.Web.Services.Storage;
using floQ.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace floQ.Web.Api.V1;

/// <summary>
/// Firmen-Stammdaten des Rechnungs-Ausstellers (/api/v1/company-profile).
/// White-Label-Quelle: alles hier landet auf dem Beleg-PDF (Absender, Footer,
/// Briefpapier). Genau eine Row pro Tenant (Query-Filter isoliert automatisch).
/// </summary>
public static class CompanyProfileApi
{
    private record ApiEnvelope(bool Success, object? Data, string? ErrorMessage);
    private static IResult Ok(object? data = null) => Results.Json(new ApiEnvelope(true, data, null));
    private static IResult Fail(string message, int status = 400)
        => Results.Json(new ApiEnvelope(false, null, message), statusCode: status);

    public static void MapCompanyProfileApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1/company-profile").RequireAuthorization();

        api.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var profile = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct);
            if (profile is null) return Fail("Kein Firmenprofil vorhanden.", 404);
            return Ok(new
            {
                profile.LegalName,
                profile.Street,
                profile.ZipCode,
                profile.City,
                profile.CountryCode,
                profile.VatId,
                profile.Email,
                profile.Phone,
                profile.Website,
                profile.Iban,
                profile.Bic,
                profile.BankName,
                profile.IsSmallBusiness,
                profile.TaxExemptionText,
                HasLetterhead = !string.IsNullOrEmpty(profile.LetterheadPdfPath)
            });
        });

        api.MapPut("/", async (AppDbContext db, [FromBody] UpdateProfileRequest req, CancellationToken ct) =>
        {
            var profile = await db.CompanyProfiles.FirstOrDefaultAsync(ct);
            if (profile is null) return Fail("Kein Firmenprofil vorhanden.", 404);

            profile.LegalName = req.LegalName.Trim();
            profile.Street = req.Street.Trim();
            profile.ZipCode = req.ZipCode.Trim();
            profile.City = req.City.Trim();
            profile.CountryCode = string.IsNullOrWhiteSpace(req.CountryCode) ? "AT" : req.CountryCode.Trim().ToUpperInvariant();
            profile.VatId = Trimmed(req.VatId);
            profile.Email = Trimmed(req.Email);
            profile.Phone = Trimmed(req.Phone);
            profile.Website = Trimmed(req.Website);
            profile.Iban = Trimmed(req.Iban)?.Replace(" ", "");
            profile.Bic = Trimmed(req.Bic);
            profile.BankName = Trimmed(req.BankName);
            profile.IsSmallBusiness = req.IsSmallBusiness;
            profile.TaxExemptionText = Trimmed(req.TaxExemptionText);
            profile.UpdatedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
            return Ok();
        });

        // Briefpapier-Upload: Vektor-PDF, wird als Hintergrund unter jedes
        // Beleg-PDF gelegt. Fester tenant-relativer Pfad (eine Datei je Tenant).
        api.MapPost("/letterhead", async (AppDbContext db, UploadStorage storage, ITenantContext tenant,
            IFormFile file, CancellationToken ct) =>
        {
            if (file.Length == 0) return Fail("Leere Datei.");
            if (file.Length > 10 * 1024 * 1024) return Fail("Briefpapier darf maximal 10 MB groß sein.");
            if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                return Fail("Bitte ein PDF hochladen (Vektor-Briefpapier).");

            var profile = await db.CompanyProfiles.FirstOrDefaultAsync(ct);
            if (profile is null) return Fail("Kein Firmenprofil vorhanden.", 404);

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);

            const string relativePath = "letterhead/briefpapier.pdf";
            await storage.SaveAsync(tenant.TenantId, relativePath, ms.ToArray(), ct);

            profile.LetterheadPdfPath = relativePath;
            profile.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Ok();
        }).DisableAntiforgery();

        api.MapDelete("/letterhead", async (AppDbContext db, UploadStorage storage, ITenantContext tenant,
            CancellationToken ct) =>
        {
            var profile = await db.CompanyProfiles.FirstOrDefaultAsync(ct);
            if (profile is null) return Fail("Kein Firmenprofil vorhanden.", 404);

            if (!string.IsNullOrEmpty(profile.LetterheadPdfPath))
                storage.Delete(tenant.TenantId, profile.LetterheadPdfPath);

            profile.LetterheadPdfPath = null;
            profile.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Ok();
        });
    }

    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public sealed record UpdateProfileRequest(
        string LegalName, string Street, string ZipCode, string City, string? CountryCode,
        string? VatId, string? Email, string? Phone, string? Website,
        string? Iban, string? Bic, string? BankName,
        bool IsSmallBusiness, string? TaxExemptionText);
}
