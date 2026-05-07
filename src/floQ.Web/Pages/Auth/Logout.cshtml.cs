using floQ.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace floQ.Web.Pages.Auth;

public class LogoutModel : PageModel
{
    public async Task<IActionResult> OnGetAsync()
    {
        await SignInService.SignOutAsync(HttpContext);
        return Redirect("/");
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await SignInService.SignOutAsync(HttpContext);
        return Redirect("/");
    }
}
