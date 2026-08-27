using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Realtime;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Tournaments;

[Authorize]
public class MatchModel(TournamentMatchService matchService, IRealtimePublisher? realtimePublisher = null) : PageModel
{
    public TournamentMatchWorkspace Workspace { get; private set; } = null!;
    [BindProperty] public List<int> BladeIds { get; set; } = [];
    [BindProperty] public List<int> OrderedUserIds { get; set; } = [];
    [BindProperty] public int NewRepresentativeUserId { get; set; }
    [BindProperty] public BattleSide SideA { get; set; } = BattleSide.B;
    [BindProperty] public string? ForfeitReason { get; set; }
    [BindProperty] public int AbsentEntryId { get; set; }
    [BindProperty] public string? NoShowReason { get; set; }
    [BindProperty] public bool ConfirmNoShow { get; set; }
    [BindProperty] public string? VoidReason { get; set; }
    [BindProperty] public bool ConfirmDownstreamReset { get; set; }

    public async Task<IActionResult> OnGetAsync(int id) => await LoadAsync(id) ? Page() : NotFound();

    public async Task<IActionResult> OnGetPollAsync(int id)
    {
        var workspace = await matchService.GetWorkspaceAsync(id, User.GetRequiredUserId());
        return workspace is null
            ? NotFound()
            : new JsonResult(new { token = workspace.PollToken, status = workspace.Match.Status.ToString() });
    }

    public async Task<IActionResult> OnPostRespondAsync(int id, bool accept)
        => await RedirectWithAsync(await matchService.RespondParticipationAsync(id, User.GetRequiredUserId(), accept), id,
            accept ? "已確認出賽。" : "已拒絕出賽，本場將以不戰勝處理。");

    public async Task<IActionResult> OnPostSubmitLineupAsync(int id)
        => await RedirectWithAsync(await matchService.SubmitLineupAsync(id, User.GetRequiredUserId(), BladeIds), id, "陣容已密封提交。");

    public async Task<IActionResult> OnPostSubmitTeamOrderAsync(int id)
        => await RedirectWithAsync(await matchService.SubmitTeamOrderAsync(id, User.GetRequiredUserId(), OrderedUserIds), id, "本隊出戰順序已密封提交。");

    public async Task<IActionResult> OnPostAssignRepresentativeAsync(int id)
        => await RedirectWithAsync(await matchService.AssignMatchRepresentativeAsync(id, User.GetRequiredUserId(), NewRepresentativeUserId), id, "本場代表人已更新。");

    public async Task<IActionResult> OnPostConfirmLineupAsync(int id)
        => await RedirectWithAsync(await matchService.ConfirmLineupAsync(id, User.GetRequiredUserId()), id, "已確認公開陣容。");

    public async Task<IActionResult> OnPostSubmitReorderAsync(int id)
        => await RedirectWithAsync(await matchService.SubmitReorderAsync(id, User.GetRequiredUserId(), BladeIds), id, "本組陀螺順序已密封提交。");

    public async Task<IActionResult> OnPostSubmitTeamReorderAsync(int id)
        => await RedirectWithAsync(await matchService.SubmitTeamReorderOrderAsync(id, User.GetRequiredUserId(), OrderedUserIds), id, "本組隊員順序已密封提交。");

    public async Task<IActionResult> OnPostStartAsync(int id)
    {
        var result = await matchService.AssignSidesAndStartAsync(id, User.GetRequiredUserId(), SideA);
        if (result.Succeeded)
        {
            await PublishMatchStateAsync(id);
            return RedirectToPage("/Battles/Battle", new { id = result.Value });
        }
        TempData["Error"] = result.Error;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostForfeitAsync(int id)
        => await RedirectWithAsync(await matchService.ForfeitAsync(id, User.GetRequiredUserId(), ForfeitReason), id,
            "已完成本場棄權，對手獲勝且賽程已推進。");

    public async Task<IActionResult> OnPostDeclareNoShowAsync(int id)
        => await RedirectWithAsync(await matchService.DeclareNoShowAsync(
            id, User.GetRequiredUserId(), AbsentEntryId, NoShowReason, ConfirmNoShow), id,
            "未到判定已完成；對手以不戰勝獲勝，未建立虛構比分。");

    public async Task<IActionResult> OnPostVoidAndReopenAsync(int id)
        => await RedirectWithAsync(await matchService.VoidAndReopenAsync(
            id, User.GetRequiredUserId(), VoidReason, ConfirmDownstreamReset), id,
            "原 Battle 已保留為 Voided；雙方須重新接受出賽並重新選擇陀螺。");

    private async Task<bool> LoadAsync(int id)
    {
        var workspace = await matchService.GetWorkspaceAsync(id, User.GetRequiredUserId());
        if (workspace is null) return false;
        Workspace = workspace;
        return true;
    }

    private async Task<IActionResult> RedirectWithAsync(ServiceResult result, int id, string success)
    {
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? success : result.Error;
        if (result.Succeeded) await PublishMatchStateAsync(id);
        return RedirectToPage(new { id });
    }

    private async Task PublishMatchStateAsync(int matchId)
    {
        if (realtimePublisher is null) return;
        var workspace = await matchService.GetWorkspaceAsync(matchId, User.GetRequiredUserId());
        if (workspace is null) return;
        var match = workspace.Match;
        var userIds = match.Participants.Select(x => x.UserId).Append(match.Tournament.OrganizerUserId).Distinct();
        var targetUrl = match.Status is TournamentMatchStatus.InProgress or TournamentMatchStatus.VictoryPendingCompletion && match.Battle is not null
            ? $"/Battles/Battle/{match.Battle.Id}"
            : $"/Tournaments/Match/{match.Id}";
        await realtimePublisher.PublishUsersAsync(userIds, "match-state", new
        {
            matchId = match.Id,
            battleId = match.Battle?.Id,
            status = match.Status.ToString(),
            targetUrl
        });
    }
}
