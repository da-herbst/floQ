using Fido2NetLib;
using floQ.Web.Auth;
using floQ.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace floQ.Web.Pages.Auth;

// Passkey-Handler: WebAuthn-Antwort ist kryptografisch an Origin gebunden — CSRF-immun.
// Code-Handler: [FromBody]-JSON erzwingt Content-Type application/json,
// den ein Cross-Site-Formular nicht senden kann — Login-CSRF damit abgedeckt.
[IgnoreAntiforgeryToken]
public class LoginModel(
    IPasskeyService passkeys,
    LoginCodeService loginCodes,
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

    public class CodeBeginRequest
    {
        public string Email { get; set; } = "";
    }

    /// <summary>E-Mail-Einmalcode anfordern (Passkey-Fallback). Antwortet
    /// IMMER gleich — ob die Mail ein Konto hat, wird nicht verraten.</summary>
    public async Task<IActionResult> OnPostCodeBeginAsync(
        [FromBody] CodeBeginRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email))
            return new JsonResult(new { success = false, errorMessage = "Bitte E-Mail-Adresse eingeben." });

        try
        {
            await loginCodes.BeginAsync(req.Email, ct);
        }
        catch (Exception ex)
        {
            // Auch Versandfehler nicht nach außen tragen (Enumeration/Details).
            log.LogWarning(ex, "CodeBegin fehlgeschlagen");
        }

        return new JsonResult(new
        {
            success = true,
            data = new { message = "Wenn ein Konto existiert, wurde ein Code an diese Adresse gesendet." },
            errorMessage = (string?)null,
        });
    }

    public class CodeCompleteRequest
    {
        public string Email { get; set; } = "";
        public string Code { get; set; } = "";
    }

    public async Task<IActionResult> OnPostCodeCompleteAsync(
        [FromBody] CodeCompleteRequest req, CancellationToken ct)
    {
        var userId = await loginCodes.CompleteAsync(req.Email ?? "", req.Code ?? "", ct);
        if (userId is null)
            return new JsonResult(new { success = false, errorMessage = "Code ungültig oder abgelaufen." });

        await SignInService.SignInAsync(HttpContext, db, userId.Value, ct);
        return new JsonResult(new { success = true, data = new { redirect = "/" }, errorMessage = (string?)null });
    }
}
