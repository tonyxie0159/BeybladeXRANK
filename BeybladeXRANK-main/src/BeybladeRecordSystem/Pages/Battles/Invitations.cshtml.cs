using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Battles;

[Authorize]
public class InvitationsModel(QuickBattleFlowService flowService) : PageModel
{
    public IReadOnlyList<QuickBattleInvitation> Incoming { get; private set; } = [];
    public IReadOnlyList<QuickBattleInvitation> Outgoing { get; private set; } = [];
    public IReadOnlyList<QuickBattleResumeItem> ActiveBattles { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var invitations = await flowService.GetInvitationsAsync(User.GetRequiredUserId());
        Incoming = invitations.Incoming;
        Outgoing = invitations.Outgoing;
        ActiveBattles = await flowService.GetActiveBattlesAsync(User.GetRequiredUserId());
    }

    public async Task<IActionResult> OnPostAcceptAsync(int id)
    {
        var result = await flowService.AcceptInvitationAsync(id, User.GetRequiredUserId());
        if (result.Succeeded)
        {
            TempData["Success"] = "邀請已接受，請密封提交你的陣容。";
            return RedirectToPage("Setup", new { id = result.Value });
        }
        TempData["Error"] = result.Error;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeclineAsync(int id) =>
        RedirectWith(await flowService.DeclineInvitationAsync(id, User.GetRequiredUserId()), "邀請已拒絕並刪除。");

    public async Task<IActionResult> OnPostWithdrawAsync(int id) =>
        RedirectWith(await flowService.WithdrawInvitationAsync(id, User.GetRequiredUserId()), "邀請已撤回並刪除。");

    private IActionResult RedirectWith(ServiceResult result, string success)
    {
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? success : result.Error;
        return RedirectToPage();
    }
}
