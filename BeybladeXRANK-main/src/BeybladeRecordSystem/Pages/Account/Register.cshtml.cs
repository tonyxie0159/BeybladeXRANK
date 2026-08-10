using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Account;

public class RegisterModel(AuthService authService) : PageModel
{
    [BindProperty] public string Account { get; set; } = string.Empty;
    [BindProperty] public string Password { get; set; } = string.Empty;
    [BindProperty] public string DisplayName { get; set; } = string.Empty;
    public async Task<IActionResult> OnPostAsync()
    {
        var result = await authService.RegisterAsync(Account, Password, DisplayName);
        if (!result.Succeeded) { ModelState.AddModelError(string.Empty, result.Error!); return Page(); }
        TempData["Success"] = "帳號已建立，請登入。";
        return RedirectToPage("/Account/Login");
    }
}
