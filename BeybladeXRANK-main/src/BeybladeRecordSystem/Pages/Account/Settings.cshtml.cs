using System.ComponentModel.DataAnnotations;
using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Account;

[Authorize]
public class SettingsModel(AuthService authService) : PageModel
{
    [BindProperty, Required(ErrorMessage = "請輸入玩家名稱。"), StringLength(64, ErrorMessage = "玩家名稱最多 64 個字元。")]
    public string DisplayName { get; set; } = string.Empty;
    public void OnGet() => DisplayName = User.Identity?.Name ?? string.Empty;
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var result = await authService.ChangeDisplayNameAsync(User.GetRequiredUserId(), DisplayName);
        if (!result.Succeeded) { ModelState.AddModelError(string.Empty, result.Error!); return Page(); }
        TempData["Success"] = "顯示名稱已更新；下次登入時會套用至導覽列。";
        return RedirectToPage();
    }
}
