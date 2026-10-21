using Listener.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Listener.Api.Pages;

[Authorize(AuthenticationSchemes = AdminCredentials.Scheme, Policy = AdminCredentials.Scheme)]
[ResponseCache(NoStore = true)]
public sealed class DispositivosModel : PageModel
{
    public async Task<IActionResult> OnPostSalirAsync()
    {
        await HttpContext.SignOutAsync(AdminCredentials.Scheme);
        return RedirectToPage("/Acceso");
    }
}
