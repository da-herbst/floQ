namespace floQ.Domain.Identity;

/// <summary>
/// Ein bei einem User registrierter Passkey (FIDO2/WebAuthn-Credential).
/// Ein User kann mehrere Passkeys haben (z.B. iCloud-Keychain + YubiKey).
/// Bewusst NICHT tenant-scoped: Login passiert vor Tenant-Auswahl.
/// </summary>
public class PasskeyCredential
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>
    /// Vom Authenticator vergebene CredentialId (binär). Wird beim Login
    /// mitgesendet und gegen die DB nachgeschlagen. Indexiert + unique.
    /// </summary>
    public byte[] CredentialId { get; set; } = Array.Empty<byte>();

    /// <summary>COSE-encoded Public Key, gespeichert wie von Fido2NetLib geliefert.</summary>
    public byte[] PublicKey { get; set; } = Array.Empty<byte>();

    /// <summary>Authenticator-Modell-GUID (für Forensik/Anzeige des Geräts).</summary>
    public Guid AaGuid { get; set; }

    /// <summary>
    /// Vom Authenticator gemeldeter Signatur-Counter. Muss beim Login monoton
    /// wachsen — Schutz gegen geklonte Authenticators.
    /// </summary>
    public uint SignCount { get; set; }

    /// <summary>Vom User vergebener Anzeigename ("MacBook Touch ID", "YubiKey").</summary>
    public string Name { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAtUtc { get; set; }
}
