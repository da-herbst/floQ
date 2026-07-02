using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace floQ.Web.Pages.Settings;

/// <summary>Tenant-SMTP-Konfiguration. API-First: reiner Consumer von /api/v1/mail-settings.</summary>
[Authorize]
public class MailModel : PageModel
{
    public void OnGet() { }
}
