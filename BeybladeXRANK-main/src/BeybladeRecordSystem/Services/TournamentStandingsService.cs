using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Domain.Tournaments;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Services;

public enum TournamentStandingPlacement
{
    Ranked,
    Champion,
    RunnerUp,
    Eliminated
}

public sealed record TournamentStandingRow(
    int Rank,
    bool IsTied,
    int EntryId,
    string DisplayName,
    int Wins,
    int Losses,
    int DirectEncounterWins,
    int GroupScoreDifference,
    int ScoreDifference,
    int PointsFor,
    int Buchholz,
    decimal OpponentWinRate,
    TournamentStandingPlacement Placement = TournamentStandingPlacement.Ranked,
    TournamentBracket? EliminationBracket = null,
    int? EliminationRoundNumber = null);

public class TournamentStandingsService(AppDbContext db)
{
    public async Task<IReadOnlyList<TournamentStandingRow>> GetStandingsAsync(int tournamentId)
    {
        var tournament = await db.Tournaments.AsNoTracking().AsSplitQuery()
            .Include(x => x.Entries)
            .Include(x => x.Matches).ThenInclude(x => x.Battle)
            .SingleOrDefaultAsync(x => x.Id == tournamentId);
        if (tournament is null) return [];
        return Calculate(tournament);
    }

    public static IReadOnlyList<TournamentStandingRow> Calculate(Tournament tournament)
    {
        if (tournament.Format is TournamentFormat.SingleElimination or TournamentFormat.DoubleElimination &&
            tournament.Status != TournamentStatus.Completed)
            return [];
        var scheduledEntryIds = tournament.Matches
            .SelectMany(x => new[] { x.SideAEntryId, x.SideBEntryId })
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToHashSet();
        var stats = tournament.Entries.Where(x => scheduledEntryIds.Contains(x.Id))
            .ToDictionary(x => x.Id, x => new StandingStat(x.Id, x.DisplayNameSnapshot));
        var completed = tournament.Matches.Where(x => x.Bracket != TournamentBracket.Playoff && IsRankedResult(x)).ToList();
        if (completed.Count == 0) return [];
        foreach (var match in completed)
        {
            if (match.WinnerEntryId is int winnerId && stats.TryGetValue(winnerId, out var winner)) winner.Wins++;
            if (match.LoserEntryId is int loserId && stats.TryGetValue(loserId, out var loser)) loser.Losses++;
            if (match.IsBye || match.SideAEntryId is not int sideAId || match.SideBEntryId is not int sideBId) continue;
            stats[sideAId].Opponents.Add(sideBId);
            stats[sideBId].Opponents.Add(sideAId);
            if (match.Battle is not { Status: BattleStatus.Completed } battle) continue;
            stats[sideAId].ScoreDifference += battle.SideAScore - battle.SideBScore;
            stats[sideAId].PointsFor += battle.SideAScore;
            stats[sideBId].ScoreDifference += battle.SideBScore - battle.SideAScore;
            stats[sideBId].PointsFor += battle.SideBScore;
        }

        foreach (var stat in stats.Values)
        {
            stat.Buchholz = stat.Opponents.Sum(id => stats[id].Wins);
            stat.OpponentWinRate = stat.Opponents.Count == 0
                ? 0
                : stat.Opponents.Average(id => (decimal)stats[id].Wins / Math.Max(1, stats[id].Wins + stats[id].Losses));
        }

        var regulationStandings = tournament.Format switch
        {
            TournamentFormat.SingleElimination => CalculateSingleElimination(stats, completed),
            TournamentFormat.DoubleElimination => CalculateDoubleElimination(stats, completed),
            TournamentFormat.Swiss => AssignRanks(
                stats.Values.OrderByDescending(x => x.Wins).ThenByDescending(x => x.Buchholz)
                    .ThenByDescending(x => x.OpponentWinRate).ThenByDescending(x => x.ScoreDifference)
                    .ThenByDescending(x => x.PointsFor).ThenBy(x => x.EntryId).ToList(),
                tournament.Format),
            TournamentFormat.RoundRobin => AssignRanks(OrderRoundRobin(stats, completed), tournament.Format),
            _ => []
        };
        return tournament.Format is TournamentFormat.RoundRobin or TournamentFormat.Swiss
            ? ApplyCompletedChampionPlayoff(tournament, regulationStandings)
            : regulationStandings;
    }

    public static IReadOnlyList<int> GetRequiredChampionPlayoffEntryIds(Tournament tournament)
    {
        if (tournament.Status != TournamentStatus.InProgress ||
            tournament.Format is not (TournamentFormat.RoundRobin or TournamentFormat.Swiss) ||
            tournament.Matches.Any(x => x.Bracket == TournamentBracket.Playoff))
            return [];

        var regulationMatches = tournament.Matches.Where(x => x.Bracket != TournamentBracket.Playoff).ToList();
        if (regulationMatches.Count == 0 || regulationMatches.Any(x => !IsTerminalStatus(x.Status))) return [];
        if (tournament.Format == TournamentFormat.Swiss)
        {
            var scheduledEntryCount = regulationMatches
                .SelectMany(x => new[] { x.SideAEntryId, x.SideBEntryId })
                .Where(x => x.HasValue).Select(x => x!.Value).Distinct().Count();
            if (scheduledEntryCount < 2 || regulationMatches.Max(x => x.RoundNumber) < SwissPairingGenerator.RoundCountFor(scheduledEntryCount))
                return [];
        }

        var tiedForFirst = Calculate(tournament).Where(x => x.Rank == 1).Select(x => x.EntryId).ToList();
        return tiedForFirst.Count > 1 ? tiedForFirst : [];
    }

    private static IReadOnlyList<TournamentStandingRow> ApplyCompletedChampionPlayoff(
        Tournament tournament,
        IReadOnlyList<TournamentStandingRow> regulationStandings)
    {
        var playoffMatches = tournament.Matches.Where(x => x.Bracket == TournamentBracket.Playoff).ToList();
        if (playoffMatches.Count == 0 || playoffMatches.Any(x => !IsTerminalStatus(x.Status)))
            return regulationStandings;

        var decisiveRound = playoffMatches.Max(x => x.RoundNumber);
        var decisiveMatches = playoffMatches.Where(x => x.RoundNumber == decisiveRound && IsRankedResult(x)).ToList();
        if (decisiveMatches.Count != 1 || decisiveMatches[0].WinnerEntryId is not int championId)
            return regulationStandings;

        var tiedForFirst = regulationStandings.Where(x => x.Rank == 1).ToList();
        var champion = tiedForFirst.SingleOrDefault(x => x.EntryId == championId);
        if (champion is null || tiedForFirst.Count < 2) return regulationStandings;

        var remainingFinalists = tiedForFirst.Where(x => x.EntryId != championId).OrderBy(x => x.EntryId).ToList();
        var result = new List<TournamentStandingRow>
        {
            champion with { Rank = 1, IsTied = false, Placement = TournamentStandingPlacement.Champion }
        };
        result.AddRange(remainingFinalists.Select(x => x with
        {
            Rank = 2,
            IsTied = remainingFinalists.Count > 1,
            Placement = TournamentStandingPlacement.Ranked
        }));
        result.AddRange(regulationStandings.Where(x => x.Rank != 1));
        return result;
    }

    private static IReadOnlyList<TournamentStandingRow> CalculateSingleElimination(
        IReadOnlyDictionary<int, StandingStat> stats,
        IReadOnlyList<TournamentMatch> completed)
    {
        var eliminationMatches = completed
            .Where(x => x.Bracket == TournamentBracket.Winners && !x.IsBye && x.LoserEntryId is not null)
            .ToList();
        if (stats.Count < 2 || eliminationMatches.Count == 0) return [];

        var finalRound = eliminationMatches.Max(x => x.RoundNumber);
        var finals = eliminationMatches.Where(x => x.RoundNumber == finalRound).ToList();
        if (finals.Count != 1 || finals[0].WinnerEntryId is not int championId ||
            finals[0].LoserEntryId is not int runnerUpId || !stats.ContainsKey(championId) || !stats.ContainsKey(runnerUpId))
            return [];
        if (stats[championId].Losses != 0 || stats[runnerUpId].Losses != 1) return [];

        var eliminated = new List<(StandingStat Stat, TournamentMatch Match)>();
        foreach (var stat in stats.Values.Where(x => x.EntryId != championId && x.EntryId != runnerUpId))
        {
            var losses = eliminationMatches.Where(x => x.LoserEntryId == stat.EntryId).ToList();
            if (losses.Count != 1) return [];
            eliminated.Add((stat, losses[0]));
        }

        var rows = new List<TournamentStandingRow>
        {
            ToRow(1, false, stats[championId], TournamentStandingPlacement.Champion),
            ToRow(2, false, stats[runnerUpId], TournamentStandingPlacement.RunnerUp,
                TournamentBracket.Winners, finalRound)
        };
        foreach (var stage in eliminated.GroupBy(x => x.Match.RoundNumber).OrderByDescending(x => x.Key))
        {
            var tied = stage.Count() > 1;
            var rank = rows.Count + 1;
            rows.AddRange(stage.OrderBy(x => x.Stat.EntryId).Select(x =>
                ToRow(rank, tied, x.Stat, TournamentStandingPlacement.Eliminated,
                    TournamentBracket.Winners, x.Match.RoundNumber)));
        }
        return rows;
    }

    private static IReadOnlyList<TournamentStandingRow> CalculateDoubleElimination(
        IReadOnlyDictionary<int, StandingStat> stats,
        IReadOnlyList<TournamentMatch> completed)
    {
        if (stats.Count < 2) return [];
        var rankedFinals = completed.Where(x => x.Bracket == TournamentBracket.GrandFinal && !x.IsBye).ToList();
        var decisiveFinal = rankedFinals.SingleOrDefault(x => x.IsResetFinal) ??
            rankedFinals.SingleOrDefault(x => !x.IsResetFinal);
        if (decisiveFinal?.WinnerEntryId is not int championId || decisiveFinal.LoserEntryId is not int runnerUpId ||
            !stats.ContainsKey(championId) || !stats.ContainsKey(runnerUpId))
            return [];
        if (stats[championId].Losses > 1 || stats[runnerUpId].Losses != 2) return [];

        var orderedResults = completed.Where(x => !x.IsBye && x.LoserEntryId is not null)
            .OrderBy(x => x.SequenceNumber).ThenBy(x => x.RoundNumber).ThenBy(x => x.MatchNumber).ToList();
        var eliminated = new List<(StandingStat Stat, TournamentMatch Match)>();
        foreach (var stat in stats.Values.Where(x => x.EntryId != championId && x.EntryId != runnerUpId))
        {
            var losses = orderedResults.Where(x => x.LoserEntryId == stat.EntryId).ToList();
            if (losses.Count != 2 || losses[1].Bracket != TournamentBracket.Losers) return [];
            eliminated.Add((stat, losses[1]));
        }

        var rows = new List<TournamentStandingRow>
        {
            ToRow(1, false, stats[championId], TournamentStandingPlacement.Champion),
            ToRow(2, false, stats[runnerUpId], TournamentStandingPlacement.RunnerUp,
                TournamentBracket.GrandFinal, decisiveFinal.RoundNumber)
        };
        foreach (var stage in eliminated
                     .GroupBy(x => new { x.Match.Bracket, x.Match.RoundNumber })
                     .OrderByDescending(x => x.Key.Bracket == TournamentBracket.Losers)
                     .ThenByDescending(x => x.Key.RoundNumber))
        {
            var tied = stage.Count() > 1;
            var rank = rows.Count + 1;
            rows.AddRange(stage.OrderBy(x => x.Stat.EntryId).Select(x =>
                ToRow(rank, tied, x.Stat, TournamentStandingPlacement.Eliminated,
                    x.Match.Bracket, x.Match.RoundNumber)));
        }
        return rows;
    }

    private static TournamentStandingRow ToRow(
        int rank,
        bool isTied,
        StandingStat stat,
        TournamentStandingPlacement placement,
        TournamentBracket? eliminationBracket = null,
        int? eliminationRoundNumber = null) => new(
            rank, isTied, stat.EntryId, stat.DisplayName, stat.Wins, stat.Losses,
            stat.DirectEncounterWins, stat.GroupScoreDifference, stat.ScoreDifference,
            stat.PointsFor, stat.Buchholz, stat.OpponentWinRate, placement,
            eliminationBracket, eliminationRoundNumber);

    private static List<StandingStat> OrderRoundRobin(
        IReadOnlyDictionary<int, StandingStat> stats,
        IReadOnlyList<TournamentMatch> completed)
    {
        var ordered = new List<StandingStat>();
        foreach (var group in stats.Values.GroupBy(x => x.Wins).OrderByDescending(x => x.Key))
        {
            var tied = group.ToList();
            if (tied.Count == 2)
            {
                var ids = tied.Select(x => x.EntryId).ToHashSet();
                var headToHead = completed.SingleOrDefault(x => !x.IsBye && x.WinnerEntryId is int winner && ids.Contains(winner) &&
                    x.SideAEntryId is int a && x.SideBEntryId is int b && ids.Contains(a) && ids.Contains(b));
                if (headToHead?.WinnerEntryId is int directWinner) stats[directWinner].DirectEncounterWins = 1;
            }
            else if (tied.Count >= 3)
            {
                var ids = tied.Select(x => x.EntryId).ToHashSet();
                foreach (var match in completed.Where(x => !x.IsBye && x.SideAEntryId is int a && x.SideBEntryId is int b && ids.Contains(a) && ids.Contains(b) && x.Battle?.Status == BattleStatus.Completed))
                {
                    stats[match.SideAEntryId!.Value].GroupScoreDifference += match.Battle!.SideAScore - match.Battle.SideBScore;
                    stats[match.SideBEntryId!.Value].GroupScoreDifference += match.Battle.SideBScore - match.Battle.SideAScore;
                }
            }
            ordered.AddRange(tied.OrderByDescending(x => x.DirectEncounterWins)
                .ThenByDescending(x => x.GroupScoreDifference)
                .ThenByDescending(x => x.ScoreDifference)
                .ThenByDescending(x => x.PointsFor)
                .ThenBy(x => x.EntryId));
        }
        return ordered;
    }

    private static IReadOnlyList<TournamentStandingRow> AssignRanks(IReadOnlyList<StandingStat> ordered, TournamentFormat format)
    {
        var result = new List<TournamentStandingRow>();
        for (var index = 0; index < ordered.Count; index++)
        {
            var stat = ordered[index];
            var tiedWithPrevious = index > 0 && SameRank(stat, ordered[index - 1], format);
            var tiedWithNext = index + 1 < ordered.Count && SameRank(stat, ordered[index + 1], format);
            var rank = tiedWithPrevious ? result[^1].Rank : index + 1;
            result.Add(new TournamentStandingRow(
                rank, tiedWithPrevious || tiedWithNext, stat.EntryId, stat.DisplayName,
                stat.Wins, stat.Losses, stat.DirectEncounterWins, stat.GroupScoreDifference,
                stat.ScoreDifference, stat.PointsFor, stat.Buchholz, stat.OpponentWinRate));
        }
        return result;
    }

    private static bool SameRank(StandingStat a, StandingStat b, TournamentFormat format) => format == TournamentFormat.Swiss
        ? a.Wins == b.Wins && a.Buchholz == b.Buchholz && a.OpponentWinRate == b.OpponentWinRate &&
          a.ScoreDifference == b.ScoreDifference && a.PointsFor == b.PointsFor
        : a.Wins == b.Wins && a.DirectEncounterWins == b.DirectEncounterWins &&
          a.GroupScoreDifference == b.GroupScoreDifference && a.ScoreDifference == b.ScoreDifference && a.PointsFor == b.PointsFor;

    private static bool IsRankedResult(TournamentMatch match) => match.Status is
        TournamentMatchStatus.Completed or TournamentMatchStatus.Walkover or TournamentMatchStatus.Forfeited &&
        match.WinnerEntryId is not null;

    private static bool IsTerminalStatus(TournamentMatchStatus status) => status is
        TournamentMatchStatus.Completed or TournamentMatchStatus.Walkover or
        TournamentMatchStatus.Forfeited or TournamentMatchStatus.NotRequired;

    private sealed class StandingStat(int entryId, string displayName)
    {
        public int EntryId { get; } = entryId;
        public string DisplayName { get; } = displayName;
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int DirectEncounterWins { get; set; }
        public int GroupScoreDifference { get; set; }
        public int ScoreDifference { get; set; }
        public int PointsFor { get; set; }
        public int Buchholz { get; set; }
        public decimal OpponentWinRate { get; set; }
        public List<int> Opponents { get; } = [];
    }
}
