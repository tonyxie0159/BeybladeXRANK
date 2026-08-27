using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Account;

public class LoginModel(AuthService authService) : PageModel
{
    [BindProperty, Required(ErrorMessage = "請輸入帳號。"), StringLength(64, ErrorMessage = "帳號最多 64 個字元。")]
    public string Account { get; set; } = string.Empty;
    [BindProperty, Required(ErrorMessage = "請輸入密碼。")]
    public string Password { get; set; } = string.Empty;
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            ModelState.Remove(nameof(Password));
            Password = string.Empty;
            return Page();
        }
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
