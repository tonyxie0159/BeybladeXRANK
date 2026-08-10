using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Battles;

[Authorize]
public class ReorderModel(BattleService battleService) : PageModel
{
    public Battle Battle { get; private set; } = null!;
    [BindProperty] public List<int> PlayerAIds { get; set; } = [];
    [BindProperty] public List<int> PlayerBIds { get; set; } = [];
    public async Task<IActionResult> OnGetAsync(int id) => await LoadAsync(id) ? Page() : NotFound();
    public async Task<IActionResult> OnPostAsync(int id)
    {
        var result = await battleService.CreateReorderedLineupAsync(id, User.GetRequiredUserId(), PlayerAIds, PlayerBIds);
        if (result.Succeeded) return RedirectToPage("Battle", new { id });
        await LoadAsync(id); ModelState.AddModelError(string.Empty, result.Error!); return Page();
    }
    private async Task<bool> LoadAsync(int id) { var result = await battleService.GetBattleAsync(id, User.GetRequiredUserId()); if (!result.Succeeded) return false; Battle = result.Value!; return true; }
}
