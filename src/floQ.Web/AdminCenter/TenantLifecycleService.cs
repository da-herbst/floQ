using floQ.Domain.Tenants;
using floQ.Web.Data;
using floQ.Web.Services.Storage;
using Microsoft.EntityFrameworkCore;

namespace floQ.Web.AdminCenter;

/// <summary>
/// Mandanten-Lebenszyklus-Aktionen, ausgelöst vom batOSAdminCenter
/// (floQ hat keinen Admin-Zugang — Verwaltung läuft ausschließlich übers AC):
///
/// - <see cref="DeleteTenantAsync"/>: endgültige Löschung eines Mandanten
///   samt aller Daten (Kündigung/DSGVO). Die 5050-Abo-Rechnungen bleiben
///   davon unberührt — sie leben im AC.
/// - <see cref="ChangeUserEmailAsync"/>: Support-Rettung eines komplett
///   ausgesperrten Kunden (Postfach + Passkey verloren): neue Mail setzen,
///   optional Passkeys widerrufen → Kunde meldet sich per E-Mail-Code an
///   und registriert einen neuen Passkey.
///
/// Alle Queries laufen mit IgnoreQueryFilters + explizitem TenantId-Match —
/// der Aufruf kommt anonym vom AC, es gibt keinen TenantContext.
/// </summary>
public class TenantLifecycleService(
    AppDbContext db,
    UploadStorage uploads,
    ModuleGateService moduleGate,
    TenantShutoffService tenantShutoff,
    ILogger<TenantLifecycleService> log)
{
    /// <summary>Von der Löschung abgedeckte tenant-scoped Root-Typen
    /// (TPH-Subtypen wie Invoice/Quote hängen an Document). Der
    /// Vollständigkeits-Guard in <see cref="AssertAllTenantTypesHandled"/>
    /// wirft, sobald eine neue TenantScopedEntity hier fehlt — damit kann
    /// keine parallel entstandene Tabelle still übrig bleiben.</summary>
    private static readonly Type[] HandledTenantTypes =
    [
        typeof(Domain.Billing.ReminderInvoice),
        typeof(Domain.Billing.Payment),
        typeof(Domain.Billing.DocumentEntry),
        typeof(Domain.Billing.DocumentDistribution),
        typeof(Domain.Billing.Document),
        typeof(Domain.Billing.Customer),
        typeof(Domain.Billing.DocumentNumberConfig),
        typeof(Domain.Billing.BillingText),
        typeof(Domain.Billing.ReminderLevelConfig),
        typeof(Domain.Billing.BillingLayoutItem),
        typeof(Domain.Settings.CompanyProfile),
        typeof(Domain.Settings.TenantMailSettings),
        typeof(Domain.Settings.TenantSecret),
    ];

    /// <summary>Löscht den Mandanten endgültig: alle tenant-scoped Daten,
    /// Abo-Cache, Zuordnungen, verwaiste User (inkl. Passkeys/Login-Codes
    /// per Cascade), Upload-Dateien und die Tenant-Row selbst.
    /// false = Slug unbekannt (idempotent aus AC-Sicht).</summary>
    public async Task<bool> DeleteTenantAsync(string slug, CancellationToken ct)
    {
        AssertAllTenantTypesHandled();

        var tenant = await db.Tenants.SingleOrDefaultAsync(t => t.Slug == slug, ct);
        if (tenant is null) return false;
        var tenantId = tenant.Id;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Tenant-Daten in FK-sicherer Reihenfolge (Kinder vor Eltern:
        // ReminderInvoice→Invoice ist RESTRICT, Rest CASCADE/SET NULL).
        await db.ReminderInvoices.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.Payments.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.DocumentEntries.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.DocumentDistributions.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.Documents.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.Customers.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.DocumentNumberConfigs.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.BillingTexts.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.ReminderLevelConfigs.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.BillingLayoutItems.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.CompanyProfiles.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.TenantMailSettings.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.TenantSecrets.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);

        // Plattform-Schicht: Abo-Cache + User-Zuordnungen.
        await db.EnabledModules.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync(ct);

        var userIds = await db.UserTenants
            .Where(ut => ut.TenantId == tenantId)
            .Select(ut => ut.UserId)
            .ToListAsync(ct);
        await db.UserTenants.Where(ut => ut.TenantId == tenantId).ExecuteDeleteAsync(ct);

        // Verwaiste User löschen (Phase 1: genau einer; Mehrfach-Tenant-User
        // bleiben erhalten). Passkeys + Login-Codes hängen per Cascade dran.
        foreach (var userId in userIds)
        {
            var hasOtherTenant = await db.UserTenants.AnyAsync(ut => ut.UserId == userId, ct);
            if (!hasOtherTenant)
                await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(ct);
        }

        db.Tenants.Remove(tenant);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        uploads.DeleteTenantRoot(tenantId);
        moduleGate.Reload();
        tenantShutoff.Reload();

        log.LogWarning("Mandant '{Slug}' ({TenantId}) auf AC-Anweisung endgültig gelöscht ({Users} User).",
            slug, tenantId, userIds.Count);
        return true;
    }

    public enum EmailChangeResult { Ok, TenantNotFound, InvalidEmail, EmailTaken, Ambiguous }

    /// <summary>Setzt die Login-Mail des (einzigen) Users des Mandanten —
    /// Support-Rettung nach Identitätsprüfung durch den Hersteller.
    /// Optional werden alle Passkeys + offenen Login-Codes widerrufen
    /// (Komplett-Lockout: nichts Altes darf weiterleben).</summary>
    public async Task<EmailChangeResult> ChangeUserEmailAsync(
        string slug, string newEmail, bool revokePasskeys, CancellationToken ct)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(t => t.Slug == slug, ct);
        if (tenant is null) return EmailChangeResult.TenantNotFound;

        newEmail = newEmail.Trim().ToLowerInvariant();
        if (newEmail.Length is < 3 or > 256 || !newEmail.Contains('@'))
            return EmailChangeResult.InvalidEmail;

        var userIds = await db.UserTenants
            .Where(ut => ut.TenantId == tenant.Id)
            .Select(ut => ut.UserId)
            .ToListAsync(ct);
        if (userIds.Count != 1)
        {
            log.LogWarning("E-Mail-Änderung für '{Slug}' abgelehnt: {Count} User statt genau einem.",
                slug, userIds.Count);
            return EmailChangeResult.Ambiguous;
        }

        var userId = userIds[0];
        if (await db.Users.AnyAsync(u => u.Email == newEmail && u.Id != userId, ct))
            return EmailChangeResult.EmailTaken;

        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        var oldEmail = user.Email;
        user.Email = newEmail;

        if (revokePasskeys)
        {
            await db.PasskeyCredentials.Where(p => p.UserId == userId).ExecuteDeleteAsync(ct);
            await db.LoginCodes.Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
        }

        await db.SaveChangesAsync(ct);

        log.LogWarning("Login-Mail für Mandant '{Slug}' auf AC-Anweisung geändert ({Old} → {New}, revokePasskeys={Revoke}).",
            slug, oldEmail, newEmail, revokePasskeys);
        return EmailChangeResult.Ok;
    }

    private void AssertAllTenantTypesHandled()
    {
        var missing = db.Model.GetEntityTypes()
            .Where(t => typeof(TenantScopedEntity).IsAssignableFrom(t.ClrType) && t.BaseType is null)
            .Select(t => t.ClrType)
            .Except(HandledTenantTypes)
            .ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                "Tenant-Löschung deckt neue Entities nicht ab: "
                + string.Join(", ", missing.Select(t => t.Name))
                + " — in TenantLifecycleService.HandledTenantTypes + DeleteTenantAsync ergänzen.");
    }
}
