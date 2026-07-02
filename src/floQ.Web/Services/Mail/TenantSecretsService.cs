using floQ.Domain.Settings;
using floQ.Web.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace floQ.Web.Services.Mail;

/// <summary>
/// Verschlüsselte Tenant-Secrets (batOS-SecretsService-Muster, tenant-scoped):
/// Werte liegen DataProtection-verschlüsselt in <see cref="TenantSecret"/>.
/// Scoped statt Singleton-Cache — der Tenant kommt aus dem Request-Kontext,
/// und Secret-Zugriffe sind selten (Versand), ein DB-Read pro Zugriff ist ok.
/// </summary>
public class TenantSecretsService(AppDbContext db, IDataProtectionProvider dataProtection)
{
    private const string ProtectorPurpose = "TenantSecrets.v1";
    private readonly IDataProtector _protector = dataProtection.CreateProtector(ProtectorPurpose);

    /// <summary>Klartext-Wert lesen. Null wenn nicht vorhanden oder nicht mehr
    /// entschlüsselbar (DataProtection-Key gewechselt → Wert neu eintragen).</summary>
    public async Task<string?> GetValueAsync(string provider, string key, CancellationToken ct = default)
    {
        var row = await db.TenantSecrets.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Provider == provider && s.Key == key, ct);
        if (row is null) return null;
        try
        {
            return _protector.Unprotect(row.EncryptedValue);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> ExistsAsync(string provider, string key, CancellationToken ct = default)
        => await db.TenantSecrets.AnyAsync(s => s.Provider == provider && s.Key == key, ct);

    /// <summary>Wert verschlüsselt speichern (Upsert). Leerer Wert löscht das Secret.</summary>
    public async Task SetValueAsync(string provider, string key, string? plainValue, CancellationToken ct = default)
    {
        var row = await db.TenantSecrets
            .FirstOrDefaultAsync(s => s.Provider == provider && s.Key == key, ct);

        if (string.IsNullOrEmpty(plainValue))
        {
            if (row is not null) db.TenantSecrets.Remove(row);
        }
        else
        {
            if (row is null)
            {
                row = new TenantSecret { Provider = provider, Key = key };
                db.TenantSecrets.Add(row);
            }
            row.EncryptedValue = _protector.Protect(plainValue);
            row.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}
