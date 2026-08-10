using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Battles;

[Authorize]
public class ReviseModel(BattleService battleService) : PageModel
{
    public Battle Battle { get; private set; } = null!;
    [BindProperty] public int RoundId { get; set; }
    [BindProperty] public int WinnerPlayerId { get; set; }
    [BindProperty] public ResultType ResultType { get; set; }
    [BindProperty] public string? Reason { get; set; }
    public async Task<IActionResult> OnGetAsync(int id) => await LoadAsync(id) ? Page() : NotFound();
    public async Task<IActionResult> OnPostAsync(int id)
    {
        var result = await battleService.ReviseRoundAsync(id, RoundId, User.GetRequiredUserId(), WinnerPlayerId, ResultType, Reason);
        if (result.Succeeded) return RedirectToPage("Battle", new { id });
        await LoadAsync(id); ModelState.AddModelError(string.Empty, result.Error!); return Page();
    }
    private async Task<bool> LoadAsync(int id) { var result = await battleService.GetBattleAsync(id, User.GetRequiredUserId()); if (!result.Succeeded) return false; Battle = result.Value!; return true; }
}
