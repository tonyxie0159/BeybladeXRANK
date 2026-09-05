using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Battles;

[Authorize]
public class DetailsModel(BattleService battleService) : PageModel
{
    public Battle Battle { get; private set; } = null!;
    private TournamentMatch? SourceMatch => Battle.TournamentMatch ?? Battle.VoidedTournamentMatch;
    private bool SideAIsB => Battle.SideADesignation == BattleSide.B;
    private string SideALabel => Battle.SourceType == BattleSourceType.TournamentTeam
        ? SourceMatch?.SideAEntry?.DisplayNameSnapshot ?? "A 方隊伍"
        : Battle.PlayerA?.DisplayName ?? "A 方玩家";
    private string SideBLabel => Battle.SourceType == BattleSourceType.TournamentTeam
        ? SourceMatch?.SideBEntry?.DisplayNameSnapshot ?? "B 方隊伍"
        : Battle.PlayerB?.DisplayName ?? "B 方玩家";

    public string BLabel => SideAIsB ? SideALabel : SideBLabel;
    public string XLabel => SideAIsB ? SideBLabel : SideALabel;
    public int BScore => SideAIsB ? Battle.SideAScore : Battle.SideBScore;
    public int XScore => SideAIsB ? Battle.SideBScore : Battle.SideAScore;
    public string WinnerTitle => Battle.SourceType == BattleSourceType.TournamentTeam ? "勝利隊伍" : "勝利玩家";
    public string WinnerLabel => Battle.WinningSide switch
    {
        BattleSide.B => BLabel,
        BattleSide.X => XLabel,
        _ when Battle.WinningPlayerId == Battle.PlayerAId => SideALabel,
        _ when Battle.WinningPlayerId == Battle.PlayerBId => SideBLabel,
        _ => "尚未記錄"
    };
    public int? TournamentId => SourceMatch?.TournamentId;
    public IReadOnlyList<BattleBeybladePerformance> MyBeybladePerformance { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var result = await battleService.GetBattleAsync(id, User.GetRequiredUserId());
        if (!result.Succeeded) return NotFound();
        Battle = result.Value!;

        if (Battle.Status is BattleStatus.Completed or BattleStatus.Forfeited)
        {
            MyBeybladePerformance = BuildBeybladePerformance(Battle, User.GetRequiredUserId());
            return Page();
        }

        if (Battle.SourceType != BattleSourceType.Quick && SourceMatch is not null)
            return RedirectToPage("/Tournaments/Match", new { id = SourceMatch.Id });

        return Battle.Status switch
        {
            BattleStatus.LineupSelection or BattleStatus.LineupReview or BattleStatus.LineupLocked or BattleStatus.SideSelection
                => RedirectToPage("Setup", new { id }),
            BattleStatus.ReorderSelection => RedirectToPage("Reorder", new { id }),
            _ => RedirectToPage("Battle", new { id })
        };
    }

    private static IReadOnlyList<BattleBeybladePerformance> BuildBeybladePerformance(Battle battle, int userId)
    {
        var lineups = battle.Lineups.ToDictionary(x => x.Id);
        return battle.Rounds
            .Where(x => x.Status == BattleRoundStatus.Completed && (x.PlayerAId == userId || x.PlayerBId == userId))
            .Select(round =>
            {
                var isPlayerA = round.PlayerAId == userId;
                lineups.TryGetValue(round.LineupId, out var lineup);
                return new
                {
                    Round = round,
                    BeybladeId = isPlayerA ? round.PlayerABeybladeId : round.PlayerBBeybladeId,
                    ConfigurationId = isPlayerA ? lineup?.PlayerAConfigurationId : lineup?.PlayerBConfigurationId,
                    Name = isPlayerA ? round.PlayerABeybladeNameSnapshot : round.PlayerBBeybladeNameSnapshot
                };
            })
            .GroupBy(x => new { x.BeybladeId, x.ConfigurationId, x.Name })
            .Select(group =>
            {
                var events = group.SelectMany(x => x.Round.Events).Where(x => x.IsEffective).ToList();
                var results = events.Where(x =>
                    x.EventType == BattleRoundEventType.BattleResult && x.WinnerPlayerId.HasValue).ToList();
                return new BattleBeybladePerformance(
                    group.Key.BeybladeId,
                    group.Key.ConfigurationId,
                    group.Key.Name,
                    group.Count(),
                    results.Count(x => x.WinnerPlayerId == userId),
                    results.Count(x => x.WinnerPlayerId != userId),
                    events.Where(x => x.WinnerPlayerId == userId).Sum(x => x.ScoreAwarded),
                    events.Where(x => x.WinnerPlayerId.HasValue && x.WinnerPlayerId != userId).Sum(x => x.ScoreAwarded));
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Name)
            .ToList();
    }

    public sealed record BattleBeybladePerformance(
        int BeybladeId,
        int? ConfigurationId,
        string Name,
        int RoundCount,
        int Wins,
        int Losses,
        int Score,
        int AgainstScore);
}
