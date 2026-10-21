using System.Security.Claims;
using Listener.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Listener.Api.Pages;

[EnableRateLimiting("admin-login")]
[ResponseCache(NoStore = true)]
public sealed class AccesoModel(AdminCredentials credentials) : PageModel
{
    [BindProperty] public string Username { get; set; } = "admin";
    [BindProperty] public string? Password { get; set; }
    public string? Error { get; private set; }
    public bool Configured => credentials.Load() is not null;

    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true ? RedirectToPage("/Historial") : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        var password = Password;
        Password = null;
        ModelState.Remove(nameof(Password));
        if (!Configured) { Error = "El acceso administrativo aun no esta configurado en el servidor."; return Page(); }
        if (!credentials.Verify(Username, password)) { Error = "Usuario o contrasena incorrectos."; return Page(); }
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, Username), new Claim(ClaimTypes.Role, "Administrator"),
            new Claim("admin-version", credentials.Version!)
        }, AdminCredentials.Scheme);
        await HttpContext.SignInAsync(AdminCredentials.Scheme, new ClaimsPrincipal(identity));
        return RedirectToPage("/Historial");
    }
}
