using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Battles;

[Authorize]
public class DetailsModel(BattleService battleService) : PageModel
{
    public BeybladeRecordSystem.Domain.Entities.Battle Battle { get; private set; } = null!;
    public async Task<IActionResult> OnGetAsync(int id)
    {
        var result = await battleService.GetBattleAsync(id, User.GetRequiredUserId());
        if (!result.Succeeded) return NotFound();
        Battle = result.Value!;
        return Page();
    }
    public bool IsLocked => Battle.Status == BattleStatus.LineupLocked;

    public async Task<IActionResult> OnPostStartAsync(int id)
    {
        var result = await battleService.StartBattleAsync(id, User.GetRequiredUserId());
        if (!result.Succeeded) return BadRequest(result.Error ?? "無法開始對戰。");
        return RedirectToPage("Battle", new { id });
    }
}
