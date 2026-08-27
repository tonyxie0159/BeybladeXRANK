using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using BeybladeRecordSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Tournaments;

[Authorize]
public class DetailsModel(TournamentService tournamentService, TournamentMatchService matchService, TournamentStandingsService standingsService) : PageModel
{
    public Tournament Tournament { get; private set; } = null!;
    public TournamentPublicDetailsViewModel PublicDetails { get; private set; } = null!;
    public bool IsOrganizer { get; private set; }
    public bool IsRegistered { get; private set; }
    public bool CanRegister { get; private set; }
    public bool CanReopenRegistration => IsOrganizer &&
        Tournament.Status == TournamentStatus.RegistrationOpen &&
        Tournament.RegistrationStage is TournamentRegistrationStage.Closed or
            TournamentRegistrationStage.AwaitingTeamFormation or
            TournamentRegistrationStage.ScheduleDraftCreated;
    public TournamentInvitation? ParticipantInvitation { get; private set; }
    public bool IsSystemPairingRegistered { get; private set; }
    public IReadOnlyList<TournamentEntry> SystemPairingEntries { get; private set; } = [];
    public TournamentTeamWorkspace TeamWorkspace { get; private set; } = new(null, [], []);
    public IReadOnlyList<TournamentMatchAction> ActionMatches { get; private set; } = [];
    public IReadOnlyList<TournamentStandingRow> Standings { get; private set; } = [];
    [BindProperty] public string? TeamName { get; set; }
    [BindProperty] public int InvitePlayerUserId { get; set; }
    [BindProperty] public int InviteParticipantUserId { get; set; }
    [BindProperty] public int NewRepresentativeId { get; set; }
    [BindProperty] public int FirstMemberId { get; set; }
    [BindProperty] public int SecondMemberId { get; set; }
    [BindProperty] public List<int> OrderedEntryIds { get; set; } = [];
    [BindProperty] public string? CancellationReason { get; set; }

    public async Task<IActionResult> OnGetAsync(int id) => await LoadAsync(id) ? Page() : NotFound();

    public async Task<IActionResult> OnGetPollAsync(int id)
    {
        var details = await tournamentService.GetPublicDetailsAsync(id, User.GetRequiredUserId());
        return details is null
            ? NotFound()
            : new JsonResult(new { token = details.PollToken, status = details.Status.ToString() });
    }

    public async Task<IActionResult> OnPostRegisterAsync(int id)
    {
        var result = await tournamentService.RegisterIndividualAsync(id, User.GetRequiredUserId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "報名成功。" : result.Error;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostWithdrawAsync(int id)
    {
        var result = await tournamentService.WithdrawAsync(id, User.GetRequiredUserId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "已取消報名。" : result.Error;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostCloseRegistrationAsync(int id)
    {
        var result = await tournamentService.CloseRegistrationAsync(id, User.GetRequiredUserId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "報名已關閉。" : result.Error;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostCreateTeamAsync(int id)
    {
        var result = await tournamentService.CreateTemporaryTeamAsync(id, User.GetRequiredUserId(), TeamName);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "臨時隊伍已建立，現在可以邀請隊員。" : result.Error;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostInviteTeamMemberAsync(int id, int entryId)
    {
        var result = await tournamentService.InviteTeamMemberAsync(id, entryId, User.GetRequiredUserId(), InvitePlayerUserId);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "隊伍邀請已送出。" : result.Error;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRespondInvitationAsync(int id, int invitationId, bool accept)
    {
        var result = await tournamentService.RespondToTeamInvitationAsync(invitationId, User.GetRequiredUserId(), accept);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? (accept ? "已加入隊伍。" : "已拒絕邀請。") : result.Error;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRegisterTeamAsync(int id, int entryId)
    {
        var result = await tournamentService.RegisterCompleteTeamAsync(id, entryId, User.GetRequiredUserId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "整隊已正式報名。" : result.Error;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostTransferRepresentativeAsync(int id, int entryId)
    {
        var result = await tournamentService.TransferRepresentativeAsync(id, entryId, User.GetRequiredUserId(), NewRepresentativeId);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "代表人轉讓邀請已送出。" : result.Error;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRespondRepresentativeTransferAsync(int id, int invitationId, bool accept)
    {
        var result = await tournamentService.RespondToRepresentativeTransferAsync(invitationId, User.GetRequiredUserId(), accept);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? (accept ? "已接受代表人轉讓。" : "已拒絕代表人轉讓。") : result.Error;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostLeaveTeamAsync(int id)
    {
        var result = await tournamentService.LeaveTeamAsync(id, User.GetRequiredUserId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "已退出隊伍。" : result.Error;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRegisterForPairingAsync(int id)
    {
        var result = await tournamentService.RegisterForSystemPairingAsync(id, User.GetRequiredUserId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "已登記等待系統配隊。" : result.Error;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostWithdrawFromPairingAsync(int id)
    {
        var result = await tournamentService.WithdrawFromSystemPairingAsync(id, User.GetRequiredUserId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "已取消系統配隊登記。" : result.Error;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostGenerateSystemTeamsAsync(int id)
    {
        var result = await tournamentService.GenerateSystemAssignedTeamsAsync(id, User.GetRequiredUserId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "系統配隊已完成。" : result.Error;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostReopenRegistrationAsync(int id)
    {
        var result = await tournamentService.ReopenRegistrationAsync(id, User.GetRequiredUserId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "報名已重新開放。" : result.Error;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostSwapMembersAsync(int id)
    {
        var result = await tournamentService.SwapSystemAssignedMembersAsync(id, User.GetRequiredUserId(), FirstMemberId, SecondMemberId);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "隊員交換完成。" : result.Error;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostGenerateScheduleAsync(int id)
    {
        var result = await tournamentService.GenerateScheduleDraftAsync(id, User.GetRequiredUserId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "賽程草稿已產生。" : result.Error;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostAbandonScheduleAsync(int id)
    {
        var result = await tournamentService.AbandonScheduleDraftAsync(id, User.GetRequiredUserId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "賽程草稿已放棄。" : result.Error;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostReorderScheduleAsync(int id)
    {
        var result = await tournamentService.ReorderScheduleEntriesAsync(id, User.GetRequiredUserId(), OrderedEntryIds);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "賽程位置已更新。" : result.Error;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostStartTournamentAsync(int id)
    {
        var result = await tournamentService.StartTournamentAsync(id, User.GetRequiredUserId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "比賽已正式開始。" : result.Error;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostInviteParticipantAsync(int id)
    {
        var result = await tournamentService.InviteParticipantAsync(
            id, User.GetRequiredUserId(), InviteParticipantUserId);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? "參賽邀請已送出。"
            : result.Error;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRespondParticipantInvitationAsync(
        int id,
        int invitationId,
        bool accept)
    {
        var result = await tournamentService.RespondToTournamentInvitationAsync(
            invitationId, User.GetRequiredUserId(), accept);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? accept ? "已接受參賽邀請並完成報名。" : "已拒絕參賽邀請。"
            : result.Error;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostCancelTournamentAsync(int id)
    {
        var result = await tournamentService.CancelTournamentAsync(id, User.GetRequiredUserId(), CancellationReason);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "整場比賽已取消；已完成的對局與回合資料仍會保留。" : result.Error;
        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadAsync(int id)
    {
        var userId = User.GetRequiredUserId();
        var publicDetails = await tournamentService.GetPublicDetailsAsync(id, userId);
        if (publicDetails is null) return false;
        var tournament = await tournamentService.GetDetailsAsync(id);
        if (tournament is null) return false;
        PublicDetails = publicDetails;
        Tournament = tournament;
        IsOrganizer = tournament.OrganizerUserId == userId;
        ActionMatches = await matchService.GetActionableAsync(id, userId);
        Standings = await standingsService.GetStandingsAsync(id);
        IsRegistered = tournament.Entries.Any(x => x.Status == TournamentEntryStatus.Registered &&
            (x.IndividualUserId == userId || x.Members.Any(m => m.UserId == userId)));
        CanRegister = tournament.Status == TournamentStatus.RegistrationOpen &&
            tournament.RegistrationStage == TournamentRegistrationStage.Open &&
            tournament.Mode == TournamentMode.Individual && !IsRegistered;
        if (tournament.Mode == TournamentMode.Individual)
            ParticipantInvitation = await tournamentService.GetPendingParticipantInvitationAsync(id, userId);
        if (tournament.Mode == TournamentMode.Team)
        {
            TeamWorkspace = await tournamentService.GetTeamWorkspaceAsync(id, userId);
            IsSystemPairingRegistered = tournament.RegistrationMode == TournamentRegistrationMode.SystemAssignedTeam && TeamWorkspace.Team is not null;
            if (IsOrganizer && tournament.RegistrationMode == TournamentRegistrationMode.SystemAssignedTeam)
                SystemPairingEntries = await tournamentService.GetSystemPairingEntriesForOrganizerAsync(id, userId);
        }
        return true;
    }
}
