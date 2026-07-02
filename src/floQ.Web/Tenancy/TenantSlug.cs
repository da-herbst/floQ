namespace floQ.Web.Tenancy;

/// <summary>
/// Erzeugt den stabilen Mandanten-Slug (= ShortName der AC-Instanz,
/// lowercase, [a-z0-9-], ≤ 32, unique). Basis ist der Localpart der
/// Registrierungs-Mail; Eindeutigkeit stellt der Aufrufer per DB-Check
/// und <see cref="WithSuffix"/> her. Einmal vergeben, nie wieder ändern.
/// </summary>
public static class TenantSlug
{
    /// <summary>Maximal 28 Basiszeichen, damit für Kollisions-Suffixe
    /// ("-2" … "-999") Platz unterhalb des AC-Limits (32) bleibt.</summary>
    private const int MaxBaseLength = 28;

    public static string FromEmail(string email)
    {
        var localPart = email.Split('@')[0].ToLowerInvariant();

        var chars = localPart
            .Select(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '-')
            .ToArray();
        var slug = new string(chars);

        while (slug.Contains("--"))
            slug = slug.Replace("--", "-");
        slug = slug.Trim('-');

        if (slug.Length > MaxBaseLength)
            slug = slug[..MaxBaseLength].TrimEnd('-');

        return slug.Length == 0 ? "kunde" : slug;
    }

    public static string WithSuffix(string baseSlug, int suffix) => $"{baseSlug}-{suffix}";
}
