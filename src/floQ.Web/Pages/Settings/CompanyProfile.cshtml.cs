using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace floQ.Web.Pages.Settings;

/// <summary>Firmen-Stammdaten (White-Label-Quelle des Beleg-PDFs).
/// API-First: reiner Consumer von /api/v1/company-profile.</summary>
[Authorize]
public class CompanyProfileModel : PageModel
{
    public void OnGet() { }
}
