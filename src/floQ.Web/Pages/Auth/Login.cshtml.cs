using Fido2NetLib;
using floQ.Web.Auth;
using floQ.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace floQ.Web.Pages.Auth;

[IgnoreAntiforgeryToken] // WebAuthn-Antwort ist kryptografisch an Origin gebunden — CSRF-immun.
public class LoginModel(
    IPasskeyService passkeys,
    AppDbContext db,
    ILogger<LoginModel> log) : PageModel
{
    private const string SessionKey = "fido2.login.options";

    public void OnGet() { }

    public class BeginLoginRequest
    {
        public string? Email { get; set; }
    }

    public async Task<IActionResult> OnPostBeginAsync(
        [FromBody] BeginLoginRequest req, CancellationToken ct)
    {
        try
        {
            var options = await passkeys.BeginLoginAsync(req.Email, ct);
            HttpContext.Session.SetString(SessionKey, options.ToJson());
            return new JsonResult(new { success = true, data = options });
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "BeginLogin fehlgeschlagen für {Email}", req.Email);
            return new JsonResult(new { success = false, errorMessage = ex.Message });
        }
    }

    public class CompleteLoginRequest
    {
        public AuthenticatorAssertionRawResponse Assertion { get; set; } = null!;
    }

    public async Task<IActionResult> OnPostCompleteAsync(
        [FromBody] CompleteLoginRequest req, CancellationToken ct)
    {
        try
        {
            var optionsJson = HttpContext.Session.GetString(SessionKey)
                ?? throw new InvalidOperationException("Keine aktive Anmeldung in dieser Session.");
            var options = AssertionOptions.FromJson(optionsJson);

            var userId = await passkeys.CompleteLoginAsync(req.Assertion, options, ct);

            HttpContext.Session.Remove(SessionKey);
            await SignInService.SignInAsync(HttpContext, db, userId, ct);

            return new JsonResult(new { success = true, data = new { redirect = "/" } });
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "CompleteLogin fehlgeschlagen");
            return new JsonResult(new { success = false, errorMessage = ex.Message });
        }
    }
}
