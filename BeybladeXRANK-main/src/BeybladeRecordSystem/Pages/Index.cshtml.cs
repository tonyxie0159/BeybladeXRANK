using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages;

public class IndexModel(QuickBattleFlowService quickBattleFlowService) : PageModel
{
    public int PendingQuickBattleInvitations { get; private set; }
    public IReadOnlyList<QuickBattleResumeItem> ActiveQuickBattles { get; private set; } = [];

    public async Task OnGetAsync()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var userId = User.GetRequiredUserId();
            PendingQuickBattleInvitations = await quickBattleFlowService.GetIncomingInvitationCountAsync(userId);
            ActiveQuickBattles = await quickBattleFlowService.GetActiveBattlesAsync(userId);
        }
    }
}
