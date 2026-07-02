namespace floQ.Web.Services.Time;

/// <summary>
/// Zeitzonen-Vertrag von floQ (Regel: Speicherung UTC, Anzeige Wien).
/// Einzige Stelle, die die Wiener Zeitzone kennt — alle Konversionen laufen
/// hier durch. Niemals <c>DateTime.Now</c>/<c>Today</c> verwenden.
///
/// Kind-Semantik (batOS-Konvention):
/// - UTC-Werte tragen Kind=Utc.
/// - Wiener Wanduhrzeit trägt Kind=Unspecified (hat keine Server-Kind-Semantik).
/// - <see cref="ToUtc(DateTime)"/> ist idempotent: Utc bleibt, Unspecified wird
///   als Wiener Wanduhrzeit interpretiert.
/// </summary>
public static class ViennaTime
{
    /// <summary>Wiener Zeitzone — IANA "Europe/Vienna". Einzige Quelle der TZ-Identität.</summary>
    public static readonly TimeZoneInfo Zone =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna");

    /// <summary>Aktuelle Wiener Wanduhrzeit (Kind=Unspecified).</summary>
    public static DateTime Now => ToVienna(DateTime.UtcNow);

    /// <summary>Heutiges Datum in Wien (00:00 Wanduhrzeit, Kind=Unspecified).</summary>
    public static DateTime Today => Now.Date;

    /// <summary>UTC → Wiener Wanduhrzeit (für Anzeige, Kind=Unspecified).</summary>
    public static DateTime ToVienna(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    /// <summary>UTC → Wiener Wanduhrzeit (nullable Überladung).</summary>
    public static DateTime? ToVienna(DateTime? utc)
        => utc.HasValue ? ToVienna(utc.Value) : null;

    /// <summary>
    /// Beliebiger DateTime → UTC. Idempotent:
    ///   Kind=Utc          → unverändert.
    ///   Kind=Local        → ToUniversalTime() (Server läuft in UTC).
    ///   Kind=Unspecified  → als Wiener Wanduhrzeit interpretiert → UTC.
    /// </summary>
    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(value, DateTimeKind.Unspecified), Zone)
    };

    /// <summary>Beliebiger DateTime → UTC (nullable Überladung).</summary>
    public static DateTime? ToUtc(DateTime? value)
        => value.HasValue ? ToUtc(value.Value) : null;
}
