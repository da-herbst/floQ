using Fido2NetLib;
using floQ.Web.Auth;
using floQ.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace floQ.Web.Pages.Auth;

[IgnoreAntiforgeryToken] // WebAuthn-Antwort ist kryptografisch an Origin gebunden — CSRF-immun.
public class RegisterModel(
    IPasskeyService passkeys,
    AppDbContext db,
    ILogger<RegisterModel> log) : PageModel
{
    private const string SessionKey = "fido2.register.options";

    public void OnGet() { /* Form-Render, JS macht den Rest */ }

    public class BeginRegisterRequest
    {
        public string Email { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }

    public async Task<IActionResult> OnPostBeginAsync(
        [FromBody] BeginRegisterRequest req, CancellationToken ct)
    {
        try
        {
            var options = await passkeys.BeginRegistrationAsync(req.Email, req.DisplayName, ct);
            HttpContext.Session.SetString(SessionKey, options.ToJson());
            return new JsonResult(new { success = true, data = options });
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "BeginRegister fehlgeschlagen für {Email}", req.Email);
            return new JsonResult(new { success = false, errorMessage = ex.Message });
        }
    }

    public class CompleteRegisterRequest
    {
        public AuthenticatorAttestationRawResponse Attestation { get; set; } = null!;
        public string CredentialName { get; set; } = "";
    }

    public async Task<IActionResult> OnPostCompleteAsync(
        [FromBody] CompleteRegisterRequest req, CancellationToken ct)
    {
        try
        {
            var optionsJson = HttpContext.Session.GetString(SessionKey)
                ?? throw new InvalidOperationException("Keine aktive Registrierung in dieser Session.");
            var options = CredentialCreateOptions.FromJson(optionsJson);

            var userId = await passkeys.CompleteRegistrationAsync(
                req.Attestation, options, req.CredentialName, ct);

            HttpContext.Session.Remove(SessionKey);
            await SignInService.SignInAsync(HttpContext, db, userId, ct);

            return new JsonResult(new { success = true, data = new { redirect = "/" } });
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "CompleteRegister fehlgeschlagen");
            return new JsonResult(new { success = false, errorMessage = ex.Message });
        }
    }
}
