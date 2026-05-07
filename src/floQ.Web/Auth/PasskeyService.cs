using System.Text;
using Fido2NetLib;
using Fido2NetLib.Objects;
using floQ.Domain.Identity;
using floQ.Domain.Settings;
using floQ.Domain.Tenants;
using floQ.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace floQ.Web.Auth;

/// <summary>
/// Default-Implementierung mit Fido2NetLib + EF.
///
/// User-Provisioning:
/// - Beim ersten Begin-Register-Aufruf wird der User angelegt (falls Mail unbekannt).
/// - Beim ersten Complete-Register-Aufruf wird zusätzlich ein Default-Tenant +
///   UserTenant + leeres CompanyProfile erzeugt (Auto-Provisioning Phase 1 Solo-Flow).
/// - Folgeaufrufe (zweites Gerät, neuer Passkey) registrieren ein zusätzliches
///   Credential auf demselben User, ohne neuen Tenant.
/// </summary>
public class PasskeyService(
    IFido2 fido2,
    AppDbContext db) : IPasskeyService
{
    public async Task<CredentialCreateOptions> BeginRegistrationAsync(
        string email, string displayName, CancellationToken ct)
    {
        email = NormalizeEmail(email);
        if (string.IsNullOrWhiteSpace(displayName)) displayName = email;

        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            user = new User { Email = email, DisplayName = displayName };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        var existing = await db.PasskeyCredentials
            .Where(p => p.UserId == user.Id)
            .Select(p => new PublicKeyCredentialDescriptor(p.CredentialId))
            .ToListAsync(ct);

        var fidoUser = new Fido2User
        {
            Id = user.Id.ToByteArray(),
            Name = user.Email,
            DisplayName = user.DisplayName
        };

        var authenticatorSelection = new AuthenticatorSelection
        {
            ResidentKey = ResidentKeyRequirement.Required,
            UserVerification = UserVerificationRequirement.Required
        };

        return fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = fidoUser,
            ExcludeCredentials = existing,
            AuthenticatorSelection = authenticatorSelection,
            AttestationPreference = AttestationConveyancePreference.None
        });
    }

    public async Task<Guid> CompleteRegistrationAsync(
        AuthenticatorAttestationRawResponse attestation,
        CredentialCreateOptions originalOptions,
        string credentialName,
        CancellationToken ct)
    {
        var userId = new Guid(originalOptions.User.Id);

        var result = await fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
        {
            AttestationResponse = attestation,
            OriginalOptions = originalOptions,
            IsCredentialIdUniqueToUserCallback = async (args, innerCt) =>
                !await db.PasskeyCredentials.AnyAsync(p => p.CredentialId == args.CredentialId, innerCt)
        }, ct);

        var credential = new PasskeyCredential
        {
            UserId = userId,
            CredentialId = result.Id,
            PublicKey = result.PublicKey,
            AaGuid = result.AaGuid,
            SignCount = result.SignCount,
            Name = string.IsNullOrWhiteSpace(credentialName) ? "Passkey" : credentialName.Trim()
        };
        db.PasskeyCredentials.Add(credential);

        // Auto-Provisioning: erster Passkey für diesen User → Default-Tenant anlegen.
        var hasTenant = await db.UserTenants.AnyAsync(ut => ut.UserId == userId, ct);
        if (!hasTenant)
        {
            var user = await db.Users.SingleAsync(u => u.Id == userId, ct);

            var tenant = new Tenant { Name = user.Email };
            db.Tenants.Add(tenant);

            db.UserTenants.Add(new UserTenant
            {
                UserId = userId,
                TenantId = tenant.Id,
                IsDefault = true,
                Role = TenantRole.Owner
            });

            // CompanyProfile ist TenantScopedEntity → SaveChanges-Hook würde TenantId
            // aus dem (noch unaufgelösten) TenantContext lesen wollen. Daher hier explizit setzen.
            db.CompanyProfiles.Add(new CompanyProfile
            {
                TenantId = tenant.Id,
                LegalName = "" // User füllt später unter /Settings/CompanyProfile
            });
        }

        await db.SaveChangesAsync(ct);
        return userId;
    }

    public async Task<AssertionOptions> BeginLoginAsync(string? email, CancellationToken ct)
    {
        var allowedCredentials = new List<PublicKeyCredentialDescriptor>();

        if (!string.IsNullOrWhiteSpace(email))
        {
            email = NormalizeEmail(email);
            var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);
            if (user is not null)
            {
                allowedCredentials = await db.PasskeyCredentials
                    .Where(p => p.UserId == user.Id)
                    .Select(p => new PublicKeyCredentialDescriptor(p.CredentialId))
                    .ToListAsync(ct);
            }
            // Wenn unbekannte Mail: trotzdem leere Options zurückgeben — kein User-Enumeration-Leak.
        }

        return fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = allowedCredentials,
            UserVerification = UserVerificationRequirement.Required
        });
    }

    public async Task<Guid> CompleteLoginAsync(
        AuthenticatorAssertionRawResponse assertion,
        AssertionOptions originalOptions,
        CancellationToken ct)
    {
        // assertion.Id ist base64url-string; assertion.RawId ist die rohe ByteSequenz,
        // die mit unserer DB-Spalte (BYTEA) verglichen werden kann.
        var credentialId = assertion.RawId;
        var credential = await db.PasskeyCredentials
            .SingleOrDefaultAsync(p => p.CredentialId == credentialId, ct)
            ?? throw new InvalidOperationException("Unbekanntes Credential.");

        var result = await fido2.MakeAssertionAsync(new MakeAssertionParams
        {
            AssertionResponse = assertion,
            OriginalOptions = originalOptions,
            StoredPublicKey = credential.PublicKey,
            StoredSignatureCounter = credential.SignCount,
            IsUserHandleOwnerOfCredentialIdCallback = (args, _) =>
            {
                var userId = new Guid(args.UserHandle);
                return Task.FromResult(userId == credential.UserId);
            }
        }, ct);

        credential.SignCount = result.SignCount;
        credential.LastUsedAtUtc = DateTime.UtcNow;

        var user = await db.Users.SingleAsync(u => u.Id == credential.UserId, ct);
        user.LastLoginAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return credential.UserId;
    }

    private static string NormalizeEmail(string email)
        => email.Trim().ToLowerInvariant();
}
