using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Pages.Battles;

[Authorize]
public class CreateModel(AppDbContext db, QuickBattleFlowService quickBattleFlowService) : PageModel
{
    public List<User> Opponents { get; private set; } = [];
    [BindProperty] public int OpponentId { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var result = await quickBattleFlowService.SendInvitationAsync(User.GetRequiredUserId(), OpponentId);
        if (!result.Succeeded)
        {
            await LoadAsync();
            ModelState.AddModelError(string.Empty, result.Error!);
            return Page();
        }
        TempData["Success"] = "快速對戰邀請已送出。";
        return RedirectToPage("Invitations");
    }

    private async Task LoadAsync()
    {
        var currentUserId = User.GetRequiredUserId();
        Opponents = await db.Users.Where(x => x.Id != currentUserId).OrderBy(x => x.DisplayName).ToListAsync();
    }
}
