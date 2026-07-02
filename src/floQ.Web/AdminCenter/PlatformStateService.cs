using floQ.Domain.Platform;
using floQ.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace floQ.Web.AdminCenter;

/// <summary>
/// In-Memory-Cache über der <see cref="PlatformState"/>-Singleton-Row.
/// Singleton — die Shutoff-Middleware liest pro Request hieraus, ohne
/// DB-Roundtrip. Schreiber (SyncService, Shutoff-Webhook) aktualisieren
/// die Row und rufen <see cref="Reload"/>.
/// </summary>
public class PlatformStateService(IServiceScopeFactory scopeFactory)
{
    private volatile CachedState _state = new(false, "", "");

    private sealed record CachedState(bool ShutoffActive, string ShutoffReason, string ShutoffAt);

    public bool ShutoffActive => _state.ShutoffActive;
    public string ShutoffReason => _state.ShutoffReason;
    public string ShutoffAt => _state.ShutoffAt;

    /// <summary>Lädt die Singleton-Row neu in den Cache. Beim App-Start und
    /// nach jedem Schreiben aufrufen.</summary>
    public void Reload()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = db.Set<PlatformState>().AsNoTracking()
            .SingleOrDefault(s => s.Id == PlatformState.SingletonId);
        _state = row is null
            ? new CachedState(false, "", "")
            : new CachedState(row.ShutoffActive, row.ShutoffReason, row.ShutoffAt);
    }

    /// <summary>Upsert der Singleton-Row + Cache-Reload. Zentraler
    /// Schreibpfad für SyncService und Shutoff-Webhook.</summary>
    public async Task WriteShutoffAsync(
        AppDbContext db, bool active, string reason, string at, CancellationToken ct)
    {
        var row = await db.Set<PlatformState>()
            .SingleOrDefaultAsync(s => s.Id == PlatformState.SingletonId, ct);
        if (row is null)
        {
            row = new PlatformState();
            db.Add(row);
        }
        row.ShutoffActive = active;
        row.ShutoffReason = reason;
        row.ShutoffAt = at;
        row.LastSyncAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        Reload();
    }
}
