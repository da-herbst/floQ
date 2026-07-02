namespace floQ.Web.AdminCenter;

/// <summary>
/// Verbindung zum batOSAdminCenter (zentrale Abo-Verwaltung aller
/// 5050-Software-Produkte). floQ meldet sich dort als Produkt "floq" an —
/// und zwar mit <b>jedem Tenant als eigener AC-Instanz</b>: der Tenant-Slug
/// ist der AC-ShortName. So sieht der Hersteller jeden registrierten
/// Abonnenten im AC und schaltet Abos/Shutoff pro Kunde.
///
/// Vertrag identisch zum batOS-Core-Pattern (Push/Pull-Prinzip):
/// - floQ pullt periodisch je Tenant den Zustand (Pull = einzige
///   Wahrheitsquelle, AC-Ausfall kostet nie Konsistenz, nur Latenz —
///   letzter Cache gilt).
/// - AC pusht bei Änderungen einen datenlosen Anstoß auf
///   POST /api/platform/sync bzw. POST /api/admincenter/shutoff. Beide
///   tragen keine Instanz-Identität (alle Tenants teilen denselben Host)
///   und werden daher nur als Trigger fürs sofortige Pullen behandelt.
///
/// Auth: shared PlatformKey im Header X-Platform-Key, dazu je Tenant
/// X-Instance-ShortName (= Slug) / X-Instance-Host / X-Instance-DisplayName.
///
/// Wenn PlatformKey leer ist, läuft floQ normal weiter — der Sync-Service
/// loggt eine Warnung und bleibt untätig (Dev-Betrieb).
/// </summary>
public class AdminCenterOptions
{
    public const string SectionName = "AdminCenter";

    public string BaseUrl { get; set; } = "https://admin.batos.at";
    public string PlatformKey { get; set; } = "";

    /// <summary>Öffentliche Domain dieses Deployments OHNE Schema
    /// (z.B. "floq.at") — dorthin feuert das AC seine Pushes. Gilt für
    /// alle Tenant-Instanzen gemeinsam.</summary>
    public string Host { get; set; } = "";

    /// <summary>Intervall zwischen Pulls. Reiner Fallback — Änderungen
    /// kommen primär per AC-Push sofort an.</summary>
    public TimeSpan PullInterval { get; set; } = TimeSpan.FromMinutes(5);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(PlatformKey);
}
