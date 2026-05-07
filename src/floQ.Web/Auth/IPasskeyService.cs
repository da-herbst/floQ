using Fido2NetLib;

namespace floQ.Web.Auth;

/// <summary>
/// Wrapper um Fido2NetLib + DB-Persistenz. Trennt die WebAuthn-Mechanik
/// (Challenge erzeugen, Antwort verifizieren) von der Razor-/Endpoint-Schicht.
/// </summary>
public interface IPasskeyService
{
    /// <summary>
    /// Schritt 1 der Registrierung: erzeugt CredentialCreateOptions für den Browser.
    /// Legt User on-the-fly an, falls Mail noch unbekannt ist (Signup-Flow).
    /// Die Options werden vom Aufrufer in der Session zwischengespeichert
    /// und bei Schritt 2 wieder eingereicht.
    /// </summary>
    Task<CredentialCreateOptions> BeginRegistrationAsync(string email, string displayName, CancellationToken ct);

    /// <summary>
    /// Schritt 2 der Registrierung: verifiziert die Browser-Antwort und persistiert
    /// das neue Credential. Liefert die UserId zurück, damit der Aufrufer einloggen kann.
    /// </summary>
    Task<Guid> CompleteRegistrationAsync(
        AuthenticatorAttestationRawResponse attestation,
        CredentialCreateOptions originalOptions,
        string credentialName,
        CancellationToken ct);

    /// <summary>
    /// Schritt 1 des Logins: erzeugt AssertionOptions für den Browser.
    /// Wenn email == null: discoverable credential / passkey autofill.
    /// </summary>
    Task<AssertionOptions> BeginLoginAsync(string? email, CancellationToken ct);

    /// <summary>
    /// Schritt 2 des Logins: verifiziert die Browser-Antwort gegen das gespeicherte
    /// Public-Key-Material, aktualisiert SignCount/LastUsedAt, liefert die UserId.
    /// </summary>
    Task<Guid> CompleteLoginAsync(
        AuthenticatorAssertionRawResponse assertion,
        AssertionOptions originalOptions,
        CancellationToken ct);
}
