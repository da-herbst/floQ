using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace floQ.Web.Pages.Billing;

/// <summary>Beleg-Workbench (Editor + Vorschau + Lebenszyklus).
/// API-First: reiner Consumer von /api/v1, das Model bleibt leer —
/// die Beleg-Id kommt als Query-Parameter und wird im JS aufgelöst.</summary>
[Authorize]
public class DocumentModel : PageModel
{
    public void OnGet() { }
}
