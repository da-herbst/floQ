namespace floQ.Web.AdminCenter;

/// <summary>
/// Verbindung zum batOSAdminCenter (zentrale Abo-Verwaltung aller
/// 5050-Software-Produkte). floQ meldet sich dort als Produkt "floq" an;
/// das AC verwaltet Abos der floQ-Kunden und kann die Instanz stilllegen.
///
/// Vertrag identisch zum batOS-Core-Pattern (Push/Pull-Prinzip):
/// - floQ pullt periodisch den Zustand (Pull = einzige Wahrheitsquelle,
///   AC-Ausfall kostet nie Konsistenz, nur Latenz — letzter Cache gilt).
/// - AC pusht bei Änderungen einen datenlosen Anstoß auf
///   POST /api/platform/sync, der den nächsten Pull sofort auslöst.
/// - Sofort-Shutoff via POST /api/admincenter/shutoff.
///
/// Auth: shared PlatformKey im Header X-Platform-Key, dazu
/// X-Instance-ShortName / X-Instance-Host / X-Instance-DisplayName.
///
/// Wenn PlatformKey oder ShortName leer sind, läuft floQ normal weiter —
/// der Sync-Service loggt eine Warnung und bleibt untätig (Dev-Betrieb).
/// </summary>
public class AdminCenterOptions
{
    public const string SectionName = "AdminCenter";

    public string BaseUrl { get; set; } = "https://admin.batos.at";
    public string PlatformKey { get; set; } = "";

    /// <summary>Instanz-Identifier im AC. floQ ist EIN Deployment mit vielen
    /// Mandanten — ShortName identifiziert das Deployment ("floq"),
    /// nicht den einzelnen floQ-Kunden.</summary>
    public string ShortName { get; set; } = "";

    public string Host { get; set; } = "";
    public string DisplayName { get; set; } = "";

    /// <summary>Intervall zwischen Pulls. Reiner Fallback — Änderungen
    /// kommen primär per AC-Push sofort an.</summary>
    public TimeSpan PullInterval { get; set; } = TimeSpan.FromMinutes(5);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(PlatformKey)
        && !string.IsNullOrWhiteSpace(ShortName);
}
