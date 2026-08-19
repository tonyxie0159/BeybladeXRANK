using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Tournaments;

[Authorize]
public class MatchModel(TournamentMatchService matchService) : PageModel
{
    public TournamentMatchWorkspace Workspace { get; private set; } = null!;
    [BindProperty] public List<int> BladeIds { get; set; } = [];
    [BindProperty] public List<int> OrderedUserIds { get; set; } = [];
    [BindProperty] public int NewRepresentativeUserId { get; set; }
    [BindProperty] public BattleSide SideA { get; set; } = BattleSide.B;

    public async Task<IActionResult> OnGetAsync(int id) => await LoadAsync(id) ? Page() : NotFound();

    public async Task<IActionResult> OnPostRespondAsync(int id, bool accept)
        => RedirectWith(await matchService.RespondParticipationAsync(id, User.GetRequiredUserId(), accept), id,
            accept ? "已確認出賽。" : "已拒絕出賽，本場將以不戰勝處理。");

    public async Task<IActionResult> OnPostSubmitLineupAsync(int id)
        => RedirectWith(await matchService.SubmitLineupAsync(id, User.GetRequiredUserId(), BladeIds), id, "陣容已密封提交。");

    public async Task<IActionResult> OnPostSubmitTeamOrderAsync(int id)
        => RedirectWith(await matchService.SubmitTeamOrderAsync(id, User.GetRequiredUserId(), OrderedUserIds), id, "本隊出戰順序已密封提交。");

    public async Task<IActionResult> OnPostAssignRepresentativeAsync(int id)
        => RedirectWith(await matchService.AssignMatchRepresentativeAsync(id, User.GetRequiredUserId(), NewRepresentativeUserId), id, "本場代表人已更新。");

    public async Task<IActionResult> OnPostConfirmLineupAsync(int id)
        => RedirectWith(await matchService.ConfirmLineupAsync(id, User.GetRequiredUserId()), id, "已確認公開陣容。");

    public async Task<IActionResult> OnPostSubmitReorderAsync(int id)
        => RedirectWith(await matchService.SubmitReorderAsync(id, User.GetRequiredUserId(), BladeIds), id, "本組陀螺順序已密封提交。");

    public async Task<IActionResult> OnPostSubmitTeamReorderAsync(int id)
        => RedirectWith(await matchService.SubmitTeamReorderOrderAsync(id, User.GetRequiredUserId(), OrderedUserIds), id, "本組隊員順序已密封提交。");

    public async Task<IActionResult> OnPostStartAsync(int id)
    {
        var result = await matchService.AssignSidesAndStartAsync(id, User.GetRequiredUserId(), SideA);
        if (result.Succeeded) return RedirectToPage("/Battles/Battle", new { id = result.Value });
        TempData["Error"] = result.Error;
        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadAsync(int id)
    {
        var workspace = await matchService.GetWorkspaceAsync(id, User.GetRequiredUserId());
        if (workspace is null) return false;
        Workspace = workspace;
        return true;
    }

    private IActionResult RedirectWith(ServiceResult result, int id, string success)
    {
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? success : result.Error;
        return RedirectToPage(new { id });
    }
}
