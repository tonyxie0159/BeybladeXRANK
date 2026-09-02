using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using BeybladeRecordSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Statistics;

[Authorize]
public class IndexModel(StatisticsService statisticsService) : PageModel
{
    public List<UserStatisticsRowViewModel> PersonalRows { get; private set; } = [];
    public BeybladeSourceSamplesViewModel Samples { get; private set; } = new(0, 0, 0, 0);
    public StatisticsSideSamplesViewModel SideSamples { get; private set; } = new(0, 0, 0, 0);
    public List<BeybladeStatisticsViewModel> Beyblades { get; private set; } = [];
    public IReadOnlyList<OpponentStatisticsViewModel> Opponents { get; private set; } = [];
    public List<BattleHistoryViewModel> History { get; private set; } = [];
    public string View { get; private set; } = "overview";
    public string OpponentSource { get; private set; } = "quick";
    public string Sort { get; private set; } = "score-desc";
    public string Source { get; private set; } = "all";
    public string Side { get; private set; } = "all";
    public string PersonalSort { get; private set; } = "winrate-desc";
    public string PersonalSource { get; private set; } = "all";
    public string PersonalSide { get; private set; } = "all";

    public async Task OnGetAsync(
        string? sort,
        string? source,
        string? side,
        string? personalSort,
        string? personalSource,
        string? personalSide,
        string? view,
        string? opponentSource)
    {
        View = NormalizeView(view);
        OpponentSource = NormalizeOpponentSource(opponentSource);
        Sort = NormalizeSort(sort, "score-desc");
        Source = NormalizeSource(source);
        Side = NormalizeSide(side);
        PersonalSort = NormalizeSort(personalSort, "winrate-desc");
        PersonalSource = NormalizeSource(personalSource);
        PersonalSide = NormalizeSide(personalSide);
        var sourceFilter = ParseSource(Source);
        var sideFilter = ParseSide(Side);
        var userId = User.GetRequiredUserId();

        switch (View)
        {
            case "blades":
                Samples = await statisticsService.GetBeybladeSourceSamplesAsync(userId);
                SideSamples = await statisticsService.GetBeybladeSideSamplesAsync(userId, sourceFilter);
                Beyblades = await statisticsService.GetBeybladeStatisticsAsync(userId, Sort, sourceFilter, sideFilter);
                break;
            case "opponents":
                Opponents = await statisticsService.GetOpponentStatisticsAsync(
                    userId,
                    ParseSource(OpponentSource));
                break;
            case "history":
                History = await statisticsService.GetBattleHistoryAsync(userId, StatisticsSourceFilter.All);
                break;
            default:
                PersonalRows = await statisticsService.GetUserStatisticsRowsAsync(
                    userId,
                    PersonalSort,
                    ParseSource(PersonalSource),
                    ParseSide(PersonalSide));
                break;
        }
    }

    private static string NormalizeView(string? view) => view?.ToLowerInvariant() switch
    {
        "blades" => "blades",
        "opponents" => "opponents",
        "history" => "history",
        _ => "overview"
    };

    private static string NormalizeOpponentSource(string? source) => source?.ToLowerInvariant() switch
    {
        "individual" => "individual",
        "team" => "team",
        _ => "quick"
    };

    private static string NormalizeSort(string? sort, string fallback) => sort?.ToLowerInvariant() switch
    {
        "score-desc" => "score-desc",
        "score-asc" => "score-asc",
        "against-desc" => "against-desc",
        "against-asc" => "against-asc",
        "difference-desc" => "difference-desc",
        "difference-asc" => "difference-asc",
        "winrate-desc" => "winrate-desc",
        "winrate-asc" => "winrate-asc",
        "b-winrate-desc" => "b-winrate-desc",
        "b-winrate-asc" => "b-winrate-asc",
        "x-winrate-desc" => "x-winrate-desc",
        "x-winrate-asc" => "x-winrate-asc",
        _ => fallback
    };

    private static string NormalizeSource(string? source) => source?.ToLowerInvariant() switch
    {
        "quick" => "quick",
        "individual" => "individual",
        "team" => "team",
        _ => "all"
    };

    private static string NormalizeSide(string? side) => side?.ToLowerInvariant() switch
    {
        "b" => "b",
        "x" => "x",
        _ => "all"
    };

    private static StatisticsSourceFilter ParseSource(string source) => source switch
    {
        "quick" => StatisticsSourceFilter.Quick,
        "individual" => StatisticsSourceFilter.TournamentIndividual,
        "team" => StatisticsSourceFilter.TournamentTeam,
        _ => StatisticsSourceFilter.All
    };

    private static StatisticsSideFilter ParseSide(string side) => side switch
    {
        "b" => StatisticsSideFilter.B,
        "x" => StatisticsSideFilter.X,
        _ => StatisticsSideFilter.All
    };
}
