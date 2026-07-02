using System.Collections.Concurrent;
using floQ.Domain.Billing;
using floQ.Web.Data;
using floQ.Web.Services.Mail;
using floQ.Web.Services.Storage;
using floQ.Web.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace floQ.Web.Pages.Distribution;

/// <summary>
/// Öffentliche Landing-Page für die Beleg-Ansicht per Token (batOS-Muster).
/// Kein Login — der Token ist der einzige Schlüssel und trägt implizit die
/// Tenant-Identität: der Lookup läuft per IgnoreQueryFilters, danach wird der
/// TenantContext aus der gefundenen Distribution aufgelöst und alle weiteren
/// Queries sind normal tenant-isoliert.
///
/// White-Label: die Seite trägt ausschließlich den Brand des floQ-Kunden
/// (CompanyProfile) — kein floQ-Branding gegenüber dem Endkunden.
/// </summary>
[AllowAnonymous]
public class ViewModel(AppDbContext db, ITenantContext tenantContext, UploadStorage storage) : PageModel
{
    // Rate-Limit pro Token (In-Memory): großzügig, damit mehrere Empfänger
    // (Buchhaltung, Sekretariat) dieselbe Mail öffnen können — jeder Klick
    // erzeugt typisch 2–3 Hits (View + PDF-iframe + ggf. Download).
    private static readonly ConcurrentDictionary<string, List<DateTime>> AccessLog = new();
    private const int MaxAccessesPerHour = 200;

    public bool IsValid { get; set; }
    public bool IsExpired { get; set; }
    public string Token { get; set; } = "";
    public string DocumentTypeName { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string? CompanyEmail { get; set; }

    public async Task<IActionResult> OnGetAsync(string? t)
    {
        if (!CheckRateLimit(t))
            return StatusCode(429, "Zu viele Zugriffe. Bitte versuchen Sie es später erneut.");

        var dist = await ResolveDistributionAsync(t);
        if (dist is null)
        {
            IsExpired = await IsExpiredTokenAsync(t);
            IsValid = false;
            return Page();
        }

        IsValid = true;
        Token = dist.Token;

        var doc = await db.Documents.AsNoTracking().FirstAsync(d => d.Id == dist.DocumentId);
        DocumentTypeName = BillingDistributionService.GetTypeName(doc);
        DocumentNumber = doc.Number;

        var profile = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync();
        CompanyName = profile?.LegalName ?? "";
        CompanyEmail = profile?.Email;

        // Tracking: Öffnung registrieren; Status Sent → Viewed.
        var tracked = await db.DocumentDistributions.FirstAsync(d => d.Id == dist.Id);
        tracked.OpenCount++;
        tracked.LastOpenedAtUtc = DateTime.UtcNow;
        tracked.FirstOpenedAtUtc ??= DateTime.UtcNow;

        var docTracked = await db.Documents.FirstAsync(d => d.Id == dist.DocumentId);
        if (docTracked.Status == DocumentStatus.Sent)
            docTracked.Status = DocumentStatus.Viewed;

        await db.SaveChangesAsync();
        return Page();
    }

    /// <summary>PDF inline (für das iframe der Landing-Page).</summary>
    public async Task<IActionResult> OnGetPdfAsync(string? t)
    {
        if (!CheckRateLimit(t)) return NotFound();
        var dist = await ResolveDistributionAsync(t);
        if (dist?.PdfFilePath is null) return NotFound();

        var fullPath = storage.Resolve(dist.TenantId, dist.PdfFilePath);
        if (!System.IO.File.Exists(fullPath)) return NotFound();
        return PhysicalFile(fullPath, "application/pdf");
    }

    /// <summary>PDF-Download mit Tracking.</summary>
    public async Task<IActionResult> OnGetDownloadAsync(string? t)
    {
        if (!CheckRateLimit(t)) return NotFound();
        var dist = await ResolveDistributionAsync(t);
        if (dist?.PdfFilePath is null) return NotFound();

        var fullPath = storage.Resolve(dist.TenantId, dist.PdfFilePath);
        if (!System.IO.File.Exists(fullPath)) return NotFound();

        var tracked = await db.DocumentDistributions.FirstAsync(d => d.Id == dist.Id);
        tracked.DownloadCount++;
        tracked.FirstDownloadedAtUtc ??= DateTime.UtcNow;
        await db.SaveChangesAsync();

        var doc = await db.Documents.AsNoTracking().FirstAsync(d => d.Id == dist.DocumentId);
        var fileName = $"{BillingDistributionService.GetTypeName(doc)}_{doc.Number}.pdf";
        return PhysicalFile(fullPath, "application/pdf", fileName);
    }

    /// <summary>Distribution per Token laden (am Tenant-Filter vorbei) und den
    /// TenantContext auflösen — der Token IST die Tenant-Autorisierung.</summary>
    private async Task<DocumentDistribution?> ResolveDistributionAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var dist = await db.DocumentDistributions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Token == token);

        if (dist is null) return null;
        if (dist.ExpiresAtUtc.HasValue && dist.ExpiresAtUtc.Value < DateTime.UtcNow) return null;

        if (!tenantContext.IsResolved)
            tenantContext.SetTenant(dist.TenantId);
        return dist;
    }

    private async Task<bool> IsExpiredTokenAsync(string? token)
        => !string.IsNullOrWhiteSpace(token)
           && await db.DocumentDistributions.IgnoreQueryFilters()
               .AnyAsync(d => d.Token == token && d.ExpiresAtUtc != null && d.ExpiresAtUtc < DateTime.UtcNow);

    private static bool CheckRateLimit(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return true;

        var now = DateTime.UtcNow;
        var cutoff = now.AddHours(-1);
        var accesses = AccessLog.GetOrAdd(token, _ => []);

        lock (accesses)
        {
            accesses.RemoveAll(ts => ts < cutoff);
            if (accesses.Count >= MaxAccessesPerHour) return false;
            accesses.Add(now);
        }
        return true;
    }
}
