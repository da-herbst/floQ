using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace floQ.Web.Pages.Billing;

/// <summary>Dashboard Verrechnung. API-First: die Page ist reiner Consumer —
/// alle Daten kommen per JS aus /api/v1, das Model bleibt leer.</summary>
[Authorize]
public class IndexModel : PageModel
{
    public void OnGet() { }
}
