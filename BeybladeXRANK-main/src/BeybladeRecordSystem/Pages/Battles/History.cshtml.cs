using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Battles;

[Authorize]
public sealed class HistoryModel(BattleService battleService) : PageModel
{
    public Battle Battle { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var result = await battleService.GetBattleAsync(id, User.GetRequiredUserId());
        if (!result.Succeeded) return NotFound();
        Battle = result.Value!;
        return Page();
    }
}
