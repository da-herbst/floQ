namespace floQ.Web.AdminCenter;

/// <summary>
/// Weckt den <see cref="AdminCenterSyncService"/> sofort auf (Push/Pull-
/// Prinzip): Das AC ruft bei jeder Abo-Änderung POST /api/platform/sync,
/// der Endpoint signalisiert hier — der Sync-Loop bricht sein Intervall-
/// Warten ab und pullt sofort. Der Push transportiert keine Daten.
/// </summary>
public interface IAdminCenterSyncTrigger
{
    /// <summary>Signalisiert dem Sync-Loop, sofort zu pullen. Mehrfache
    /// Signale vor dem nächsten Pull werden zu einem zusammengefasst.</summary>
    void RequestSync();

    /// <summary>Wartet bis zum Signal oder Timeout (Intervall-Fallback).
    /// True = Signal, False = Timeout.</summary>
    Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct);
}

public sealed class AdminCenterSyncTrigger : IAdminCenterSyncTrigger
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void RequestSync()
    {
        try { _signal.Release(); }
        catch (SemaphoreFullException) { /* Signal steht bereits an — reicht. */ }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) =>
        _signal.WaitAsync(timeout, ct);
}
