using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace floQ.Web.Pages.Billing;

/// <summary>Beleg-Übersicht mit Filter-Aside. API-First: reiner Consumer von /api/v1.</summary>
[Authorize]
public class DocumentsModel : PageModel
{
    public void OnGet() { }
}
