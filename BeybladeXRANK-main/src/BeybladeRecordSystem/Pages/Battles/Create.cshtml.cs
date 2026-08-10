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
public class CreateModel(AppDbContext db, BattleService battleService) : PageModel
{
    public List<User> Opponents { get; private set; } = [];
    public List<Beyblade> MyBeyblades { get; private set; } = [];
    public List<Beyblade> OpponentBeyblades { get; private set; } = [];
    [BindProperty(SupportsGet = true)] public int OpponentId { get; set; }
    [BindProperty] public List<int> PlayerAIds { get; set; } = [];
    [BindProperty] public List<int> PlayerBIds { get; set; } = [];

    public async Task<IActionResult> OnGetAsync(int? opponentId)
    {
        await LoadAsync(opponentId);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var currentUserId = User.GetRequiredUserId();
        var draft = await battleService.CreateDraftAsync(currentUserId, OpponentId);
        if (!draft.Succeeded) { await LoadAsync(OpponentId); ModelState.AddModelError(string.Empty, draft.Error!); return Page(); }
        var lineup = await battleService.SetLineupAsync(draft.Value!.Id, currentUserId, PlayerAIds, PlayerBIds);
        if (!lineup.Succeeded) { await LoadAsync(OpponentId); ModelState.AddModelError(string.Empty, lineup.Error!); return Page(); }
        var locked = await battleService.LockLineupAsync(draft.Value.Id, currentUserId);
        if (!locked.Succeeded) { await LoadAsync(OpponentId); ModelState.AddModelError(string.Empty, locked.Error!); return Page(); }
        return RedirectToPage("Details", new { id = draft.Value.Id });
    }

    private async Task LoadAsync(int? opponentId)
    {
        var currentUserId = User.GetRequiredUserId();
        Opponents = await db.Users.Where(x => x.Id != currentUserId).OrderBy(x => x.DisplayName).ToListAsync();
        MyBeyblades = await db.Beyblades.Where(x => x.UserId == currentUserId && !x.IsDeleted).OrderBy(x => x.Name).ToListAsync();
        if (opponentId is > 0 && opponentId != currentUserId)
            OpponentBeyblades = await db.Beyblades.Where(x => x.UserId == opponentId && !x.IsDeleted).OrderBy(x => x.Name).ToListAsync();
    }
}
