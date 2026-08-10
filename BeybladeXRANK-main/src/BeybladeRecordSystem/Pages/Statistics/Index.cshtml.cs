using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using BeybladeRecordSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Statistics;

[Authorize]
public class IndexModel(StatisticsService statisticsService) : PageModel
{
    public UserSummaryViewModel Summary { get; private set; } = new(0, 0, 0, 0, 0, 0);
    public List<BeybladeStatisticsViewModel> Beyblades { get; private set; } = [];
    public List<OpponentStatisticsViewModel> Opponents { get; private set; } = [];
    public List<BattleHistoryViewModel> History { get; private set; } = [];
    public string Sort { get; private set; } = "score-desc";
    public async Task OnGetAsync(string? sort)
    {
        Sort = sort ?? "score-desc"; var userId = User.GetRequiredUserId();
        Summary = await statisticsService.GetUserSummaryAsync(userId);
        Beyblades = await statisticsService.GetBeybladeStatisticsAsync(userId, Sort);
        Opponents = await statisticsService.GetOpponentStatisticsAsync(userId);
        History = await statisticsService.GetBattleHistoryAsync(userId);
    }
}
