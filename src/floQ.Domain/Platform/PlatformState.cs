namespace floQ.Domain.Platform;

/// <summary>
/// Singleton-Row (Id=1): lokal gecachter Plattform-Zustand aus dem
/// batOSAdminCenter. Quelle der Wahrheit ist das AC — diese Row ist nur
/// der persistente Cache, damit der Zustand einen Neustart überlebt
/// (AC darf down sein, ohne dass floQ sein Verhalten ändert).
/// Bewusst NICHT tenant-scoped: gilt für das gesamte Deployment.
/// </summary>
public class PlatformState
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>true = AC hat die Instanz stillgelegt → 503-Wartungsseite.</summary>
    public bool ShutoffActive { get; set; }

    public string ShutoffReason { get; set; } = "";

    /// <summary>Roh-Timestamp aus dem AC (ISO-8601-String, wie geliefert).</summary>
    public string ShutoffAt { get; set; } = "";

    public DateTime? LastSyncAtUtc { get; set; }
}
