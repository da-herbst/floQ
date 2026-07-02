namespace floQ.Domain.Platform;

/// <summary>
/// Lokal gecachtes globales Asset aus dem batOSAdminCenter (z.B. das
/// Fallback-Logo). Das AC ist Source of Truth — der Sync lädt geänderte
/// Assets per ETag/If-None-Match nach und legt die Bytes hier ab
/// (DB statt Dateisystem: kein Volume nötig, Cache überlebt Neustarts,
/// AC-Ausfall kostet nie Konsistenz).
/// Bewusst NICHT tenant-scoped: globale Hersteller-Assets.
/// </summary>
public class PlatformAsset
{
    /// <summary>Asset-Key aus dem AC (z.B. "default-logo").</summary>
    public string Key { get; set; } = "";

    /// <summary>ETag der zuletzt geladenen Version — geht beim nächsten Pull
    /// als If-None-Match mit (304 = unverändert, kein Download).</summary>
    public string ETag { get; set; } = "";

    public string ContentType { get; set; } = "";

    public byte[] Content { get; set; } = [];

    public DateTime UpdatedAtUtc { get; set; }
}
