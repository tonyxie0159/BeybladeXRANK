using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Services;

public class StatisticsService(AppDbContext db)
{
    public async Task<UserSummaryViewModel> GetUserSummaryAsync(int userId)
    {
        var battles = await CompletedBattles(userId).ToListAsync();
        var wins = battles.Count(x => x.WinningPlayerId == userId);
        var losses = battles.Count - wins;
        var score = battles.Sum(x => x.PlayerAId == userId ? x.PlayerAScore : x.PlayerBScore);
        var against = battles.Sum(x => x.PlayerAId == userId ? x.PlayerBScore : x.PlayerAScore);
        var battleIds = battles.Select(x => x.Id).ToList();
        var faultLost = await db.BattleRoundEvents.Where(x => x.IsEffective && x.EventType == BattleRoundEventType.LaunchFaultPenalty && x.ActorPlayerId == userId && battleIds.Contains(x.BattleRound.BattleId)).SumAsync(x => (int?)x.ScoreAwarded) ?? 0;
        return new UserSummaryViewModel(wins, losses, Rate(wins, losses), score, against, faultLost);
    }

    public async Task<List<BeybladeStatisticsViewModel>> GetBeybladeStatisticsAsync(int userId, string? sort)
    {
        var completedRoundIds = db.BattleRounds.Where(x => x.Battle.Status == BattleStatus.Completed).Select(x => x.Id);
        var rounds = await db.BattleRounds.Include(x => x.Events).Where(x => completedRoundIds.Contains(x.Id) && (x.Battle.PlayerAId == userId || x.Battle.PlayerBId == userId)).ToListAsync();
        var blades = await db.Beyblades.Where(x => x.UserId == userId).ToDictionaryAsync(x => x.Id, x => x.Name);
        var rows = blades.Select(pair =>
        {
            var myRounds = rounds.Where(r => r.PlayerABeybladeId == pair.Key || r.PlayerBBeybladeId == pair.Key).ToList();
            var wins = myRounds.Count(r => r.Events.Any(e => e.IsEffective && e.EventType == BattleRoundEventType.BattleResult && e.WinnerPlayerId == userId));
            var losses = myRounds.Count(r => r.Events.Any(e => e.IsEffective && e.EventType == BattleRoundEventType.BattleResult && e.WinnerPlayerId != userId));
            var score = myRounds.SelectMany(r => r.Events).Where(e => e.IsEffective && e.WinnerPlayerId == userId).Sum(e => e.ScoreAwarded);
            var against = myRounds.SelectMany(r => r.Events).Where(e => e.IsEffective && e.WinnerPlayerId != userId).Sum(e => e.ScoreAwarded);
            var faultLost = myRounds.SelectMany(r => r.Events).Where(e => e.IsEffective && e.EventType == BattleRoundEventType.LaunchFaultPenalty && e.ActorPlayerId == userId).Sum(e => e.ScoreAwarded);
            return new BeybladeStatisticsViewModel(pair.Key, pair.Value, wins, losses, Rate(wins, losses), score, against, faultLost);
        });
        return Sort(rows, sort).ToList();
    }

    public async Task<List<OpponentStatisticsViewModel>> GetOpponentStatisticsAsync(int userId)
    {
        var battles = await CompletedBattles(userId).Include(x => x.PlayerA).Include(x => x.PlayerB).ToListAsync();
        return battles.GroupBy(x => x.PlayerAId == userId ? x.PlayerB : x.PlayerA).Select(group =>
        {
            var wins = group.Count(x => x.WinningPlayerId == userId); var losses = group.Count() - wins;
            var score = group.Sum(x => x.PlayerAId == userId ? x.PlayerAScore : x.PlayerBScore);
            var against = group.Sum(x => x.PlayerAId == userId ? x.PlayerBScore : x.PlayerAScore);
            return new OpponentStatisticsViewModel(group.Key.Id, group.Key.DisplayName, wins, losses, Rate(wins, losses), score, against);
        }).OrderByDescending(x => x.WinRate).ThenBy(x => x.DisplayName).ToList();
    }

    public async Task<List<OpponentBeybladeStatisticsViewModel>> GetOpponentBeybladeStatisticsAsync(int userId, int opponentId)
    {
        var rounds = await db.BattleRounds.Include(x => x.Events).Include(x => x.Battle)
            .Where(x => x.Battle.Status == BattleStatus.Completed &&
                ((x.Battle.PlayerAId == userId && x.Battle.PlayerBId == opponentId) || (x.Battle.PlayerBId == userId && x.Battle.PlayerAId == opponentId)))
            .ToListAsync();
        return rounds.GroupBy(round => round.Battle.PlayerAId == userId
                ? (round.PlayerABeybladeNameSnapshot, round.PlayerBBeybladeNameSnapshot)
                : (round.PlayerBBeybladeNameSnapshot, round.PlayerABeybladeNameSnapshot))
            .Select(group =>
            {
                var wins = group.Count(r => r.Events.Any(e => e.IsEffective && e.EventType == BattleRoundEventType.BattleResult && e.WinnerPlayerId == userId));
                var losses = group.Count(r => r.Events.Any(e => e.IsEffective && e.EventType == BattleRoundEventType.BattleResult && e.WinnerPlayerId == opponentId));
                var score = group.SelectMany(r => r.Events).Where(e => e.IsEffective && e.WinnerPlayerId == userId).Sum(e => e.ScoreAwarded);
                var against = group.SelectMany(r => r.Events).Where(e => e.IsEffective && e.WinnerPlayerId == opponentId).Sum(e => e.ScoreAwarded);
                return new OpponentBeybladeStatisticsViewModel(group.Key.Item1, group.Key.Item2, wins, losses, Rate(wins, losses), score, against);
            }).OrderByDescending(x => x.WinRate).ThenByDescending(x => x.Score).ToList();
    }

    public async Task<List<BattleHistoryViewModel>> GetBattleHistoryAsync(int userId)
    {
        var battles = await CompletedBattles(userId).Include(x => x.PlayerA).Include(x => x.PlayerB).OrderByDescending(x => x.CompletedAtUtc).ToListAsync();
        return battles.Select(x => new BattleHistoryViewModel(x.Id, (x.PlayerAId == userId ? x.PlayerB : x.PlayerA).DisplayName, x.PlayerAId == userId ? x.PlayerAScore : x.PlayerBScore, x.PlayerAId == userId ? x.PlayerBScore : x.PlayerAScore, x.WinningPlayerId == userId, x.CompletedAtUtc)).ToList();
    }

    private IQueryable<Domain.Entities.Battle> CompletedBattles(int userId) => db.Battles.Where(x => x.Status == BattleStatus.Completed && (x.PlayerAId == userId || x.PlayerBId == userId));
    private static decimal Rate(int wins, int losses) => wins + losses == 0 ? 0 : Math.Round((decimal)wins / (wins + losses), 3);
    private static IEnumerable<BeybladeStatisticsViewModel> Sort(IEnumerable<BeybladeStatisticsViewModel> source, string? sort) => sort switch
    {
        "score-asc" => source.OrderBy(x => x.Score), "against-desc" => source.OrderByDescending(x => x.AgainstScore), "against-asc" => source.OrderBy(x => x.AgainstScore),
        "winrate-desc" => source.OrderByDescending(x => x.WinRate), "winrate-asc" => source.OrderBy(x => x.WinRate), _ => source.OrderByDescending(x => x.Score)
    };
}
