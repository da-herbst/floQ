using System.Security.Claims;
using floQ.Domain.Tenants;
using floQ.Web.Data;
using floQ.Web.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace floQ.Web.Auth;

/// <summary>
/// Setzt das Auth-Cookie nach erfolgreichem Passkey-Login/-Register.
///
/// Claim-Schema:
/// - NameIdentifier = UserId (Guid as string)
/// - Email          = User.Email
/// - Name           = User.DisplayName
/// - "tid"          = TenantId des Default-Tenants (gelesen von TenantResolverMiddleware)
///
/// In Phase 1 hat jeder User genau einen Tenant → der wird als "tid" gesetzt.
/// Sobald Tenant-Switcher kommt, ergänzt diese Klasse einen Switch-Endpoint.
/// </summary>
public static class SignInService
{
    public static async Task SignInAsync(HttpContext http, AppDbContext db, Guid userId, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        var defaultTenant = await db.UserTenants
            .Where(ut => ut.UserId == userId && ut.IsDefault)
            .Select(ut => ut.TenantId)
            .SingleOrDefaultAsync(ct);

        if (defaultTenant == Guid.Empty)
            throw new InvalidOperationException(
                $"User {userId} hat keinen Default-Tenant. Auto-Provisioning fehlgeschlagen?");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName),
            new(TenantResolverMiddleware.TenantIdClaimType, defaultTenant.ToString())
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = true });
    }

    public static Task SignOutAsync(HttpContext http)
        => http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
}
