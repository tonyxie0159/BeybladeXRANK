using System.ComponentModel.DataAnnotations;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Account;

public class RegisterModel(AuthService authService) : PageModel
{
    [BindProperty, Required(ErrorMessage = "請輸入帳號。"), StringLength(64, ErrorMessage = "帳號最多 64 個字元。")]
    public string Account { get; set; } = string.Empty;
    [BindProperty, Required(ErrorMessage = "請輸入密碼。")]
    public string Password { get; set; } = string.Empty;
    [BindProperty, Required(ErrorMessage = "請輸入玩家名稱。"), StringLength(64, ErrorMessage = "玩家名稱最多 64 個字元。")]
    public string DisplayName { get; set; } = string.Empty;
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            ModelState.Remove(nameof(Password));
            Password = string.Empty;
            return Page();
        }
        var result = await authService.RegisterAsync(Account, Password, DisplayName);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            ModelState.Remove(nameof(Password));
            Password = string.Empty;
            return Page();
        }
        TempData["Success"] = "帳號已建立，請登入。";
        return RedirectToPage("/Account/Login");
    }
}
