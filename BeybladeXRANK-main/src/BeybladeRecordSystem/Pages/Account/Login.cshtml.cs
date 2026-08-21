using System.Security.Claims;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Account;

public class LoginModel(AuthService authService) : PageModel
{
    [BindProperty] public string Account { get; set; } = string.Empty;
    [BindProperty] public string Password { get; set; } = string.Empty;
    public async Task<IActionResult> OnPostAsync()
    {
        var user = await authService.LoginAsync(Account, Password);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "帳號或密碼錯誤。");
            ModelState.Remove(nameof(Password));
            Password = string.Empty;
            return Page();
        }
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.DisplayName)], CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return LocalRedirect(Url.Content("~/"));
    }
}
