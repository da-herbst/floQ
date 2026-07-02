namespace floQ.Web.AdminCenter;

/// <summary>Produktart eines Katalog-Eintrags. <see cref="Module"/> = eigene
/// Datendomäne + Dashboard (Rail-Eintrag), <see cref="Tool"/> = schlanker
/// Launcher ohne eigenes Schema. Abo und Gating sind für beide identisch —
/// der Unterschied ist nur Darstellung.</summary>
public enum ModuleKind
{
    Module = 0,
    Tool = 1,
}

/// <summary>
/// Identität eines floQ-Moduls/-Werkzeugs: was es in dieser Code-Version gibt
/// und wie es aussieht. Wird beim Katalog-Push (POST …/module-catalog) ans
/// AC gemeldet, damit dort eine tippfehlerfreie Auswahlliste statt
/// Freitext-Keys existiert. Entitlement (wer hat es abonniert) liegt getrennt
/// davon im AC und wird in <see cref="floQ.Domain.Platform.EnabledModule"/>
/// gecached.
/// </summary>
/// <param name="Key">Stabiler Key, lowercase, ≤ 64 — identisch mit dem
/// AC-Abo. Einmal gewählt, nie wieder ändern.</param>
/// <param name="Route">Einstiegs-Route für Nav und Katalog-Push.</param>
/// <param name="RoutePrefix">Pfad-Präfix fürs serverseitige Gating: Requests
/// unterhalb dieses Präfixes beantwortet die <see cref="ModuleGateMiddleware"/>
/// ohne aktives Abo mit 503.</param>
public sealed record ModuleDescriptor(
    string Key,
    ModuleKind Kind,
    string Title,
    string Subtitle,
    string Description,
    string Icon,
    string IconColor,
    string Route,
    string RoutePrefix,
    string Audience);

/// <summary>
/// Statischer Modul-Katalog dieser floQ-Version — Single Source of Truth für
/// die Modul-Identität. floQ V1 ist reiner Core (Beleg-Domäne) ohne
/// abonnierbare Module; der Katalog ist daher leer. Neue Module/Werkzeuge
/// werden hier eingetragen und beim nächsten Prozess-Start automatisch ans
/// AC gepusht (Replace-Semantik: das AC kennt danach exakt diese Keys).
/// </summary>
public sealed class ModuleCatalog
{
    public IReadOnlyList<ModuleDescriptor> All { get; } = [];

    public ModuleDescriptor? FindByKey(string key) =>
        All.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase));
}
