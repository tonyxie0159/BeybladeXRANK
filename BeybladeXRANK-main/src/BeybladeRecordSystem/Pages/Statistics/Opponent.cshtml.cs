using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using BeybladeRecordSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Statistics;

[Authorize]
public class OpponentModel(AppDbContext db, StatisticsService statisticsService) : PageModel
{
    public string OpponentDisplayName { get; private set; } = string.Empty;
    public List<OpponentBeybladeStatisticsViewModel> Rows { get; private set; } = [];
    public string Source { get; private set; } = "all";
    public string SourceLabel { get; private set; } = "全部來源";

    public async Task<IActionResult> OnGetAsync(int id, string? source)
    {
        if (id == User.GetRequiredUserId()) return NotFound();
        var opponent = await db.Users.FindAsync(id);
        if (opponent is null) return NotFound();
        Source = NormalizeSource(source);
        var sourceFilter = ParseSource(Source);
        OpponentDisplayName = opponent.DisplayName;
        SourceLabel = GetSourceLabel(sourceFilter);
        Rows = await statisticsService.GetOpponentBeybladeStatisticsAsync(User.GetRequiredUserId(), id, sourceFilter);
        return Page();
    }

    private static string NormalizeSource(string? source) => source?.ToLowerInvariant() switch
    {
        "quick" => "quick",
        "individual" => "individual",
        "team" => "team",
        _ => "all"
    };

    private static StatisticsSourceFilter ParseSource(string source) => source switch
    {
        "quick" => StatisticsSourceFilter.Quick,
        "individual" => StatisticsSourceFilter.TournamentIndividual,
        "team" => StatisticsSourceFilter.TournamentTeam,
        _ => StatisticsSourceFilter.All
    };

    private static string GetSourceLabel(StatisticsSourceFilter source) => source switch
    {
        StatisticsSourceFilter.Quick => "快速對戰",
        StatisticsSourceFilter.TournamentIndividual => "錦標賽個人賽",
        StatisticsSourceFilter.TournamentTeam => "錦標賽團體賽實際小局",
        _ => "全部來源"
    };
}
