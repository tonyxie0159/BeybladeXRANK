using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Services;

public class StatisticsService(AppDbContext db)
{
    private const int VersionInsightMinimumRounds = 5;

    // Kept for compatibility with callers that expect the original quick-battle summary.
    public Task<UserSummaryViewModel> GetUserSummaryAsync(int userId) =>
        GetPlayerBattleSummaryAsync(userId, BattleSourceType.Quick, StatisticsSideFilter.All);

    public async Task<UserStatisticsSectionsViewModel> GetUserStatisticsSectionsAsync(
        int userId,
        StatisticsSideFilter side = StatisticsSideFilter.All)
    {
        var quick = await GetPlayerBattleSummaryAsync(userId, BattleSourceType.Quick, side);
        var individual = await GetPlayerBattleSummaryAsync(userId, BattleSourceType.TournamentIndividual, side);
        var teamResult = await GetTeamResultSummaryAsync(userId, side);
        var teamRoundPerformance = await GetTeamRoundSummaryAsync(userId, side);
        return new UserStatisticsSectionsViewModel(quick, individual, teamResult, teamRoundPerformance);
    }

    public async Task<List<UserStatisticsRowViewModel>> GetUserStatisticsRowsAsync(
        int userId,
        string? sort,
        StatisticsSourceFilter source = StatisticsSourceFilter.All,
        StatisticsSideFilter side = StatisticsSideFilter.All)
    {
        var rows = new List<UserStatisticsRowViewModel>();
        if (source is StatisticsSourceFilter.All or StatisticsSourceFilter.Quick)
        {
            rows.Add(new UserStatisticsRowViewModel(
                "quick",
                "快速對戰",
                await GetPlayerBattleSummaryAsync(userId, BattleSourceType.Quick, side)));
        }

        if (source is StatisticsSourceFilter.All or StatisticsSourceFilter.TournamentIndividual)
        {
            rows.Add(new UserStatisticsRowViewModel(
                "individual",
                "錦標賽個人賽",
                await GetPlayerBattleSummaryAsync(userId, BattleSourceType.TournamentIndividual, side)));
        }

        if (source is StatisticsSourceFilter.All or StatisticsSourceFilter.TournamentTeam)
        {
            rows.Add(new UserStatisticsRowViewModel(
                "team-result",
                "錦標賽團體賽－隊伍結果",
                await GetTeamResultSummaryAsync(userId, side)));
            rows.Add(new UserStatisticsRowViewModel(
                "team-rounds",
                "錦標賽團體賽－實際上場小局",
                await GetTeamRoundSummaryAsync(userId, side)));
        }

        return SortUserStatistics(rows, sort).ToList();
    }

    public async Task<BeybladeSourceSamplesViewModel> GetBeybladeSourceSamplesAsync(int userId)
    {
        var rounds = await ValidCompletedRounds(userId).Select(x => x.Battle.SourceType).ToListAsync();
        return new BeybladeSourceSamplesViewModel(
            rounds.Count,
            rounds.Count(x => x == BattleSourceType.Quick),
            rounds.Count(x => x == BattleSourceType.TournamentIndividual),
            rounds.Count(x => x == BattleSourceType.TournamentTeam));
    }

    public async Task<StatisticsSideSamplesViewModel> GetBeybladeSideSamplesAsync(
        int userId,
        StatisticsSourceFilter source = StatisticsSourceFilter.All)
    {
        var rounds = await ApplyRoundSourceFilter(ValidCompletedRounds(userId), source)
            .Include(x => x.Battle)
            .ToListAsync();
        var sides = rounds.Select(x => GetRoundSide(x, userId)).ToList();
        return new StatisticsSideSamplesViewModel(
            sides.Count,
            sides.Count(x => x == BattleSide.B),
            sides.Count(x => x == BattleSide.X),
            sides.Count(x => x is null));
    }

    public async Task<List<BeybladeStatisticsViewModel>> GetBeybladeStatisticsAsync(
        int userId,
        string? sort,
        StatisticsSourceFilter source = StatisticsSourceFilter.All,
        StatisticsSideFilter side = StatisticsSideFilter.All)
    {
        var rounds = await ApplyRoundSourceFilter(ValidCompletedRounds(userId), source)
            .Include(x => x.Battle)
            .Include(x => x.Events)
            .ToListAsync();
        rounds = rounds.Where(x => MatchesSide(GetRoundSide(x, userId), side)).ToList();
        var blades = await db.Beyblades
            .Where(x => x.UserId == userId)
            .ToDictionaryAsync(x => x.Id, x => x.Name + (x.UpperName == null ? "" : " · " + x.UpperName));

        var rows = blades.Select(pair => BuildBeybladeSummary(userId, pair.Key, pair.Value,
            rounds.Where(r => r.PlayerABeybladeId == pair.Key || r.PlayerBBeybladeId == pair.Key).ToList()));

        return SortBeyblades(rows, sort).ToList();
    }


    public async Task<BeybladeVersionStatistics?> GetBeybladeVersionStatisticsAsync(
        int userId, int beybladeId, StatisticsSourceFilter source = StatisticsSourceFilter.All, StatisticsSideFilter side = StatisticsSideFilter.All)
    {
        var blade = await db.Beyblades.AsNoTracking().WithConfiguration()
            .SingleOrDefaultAsync(x => x.Id == beybladeId && x.UserId == userId);
        if (blade is null) return null;
        var rounds = await ApplyRoundSourceFilter(ValidCompletedRounds(userId), source)
            .Where(x => (x.PlayerAId == userId && x.PlayerABeybladeId == beybladeId) ||
                        (x.PlayerBId == userId && x.PlayerBBeybladeId == beybladeId))
            .Include(x => x.Battle).Include(x => x.Lineup).Include(x => x.Events).ToListAsync();
        rounds = rounds.Where(x => MatchesSide(GetRoundSide(x, userId), side)).ToList();
        int? Version(BattleRound round) => round.PlayerAId == userId ? round.Lineup.PlayerAConfigurationId : round.Lineup.PlayerBConfigurationId;
        var rows = blade.Configurations.OrderByDescending(x => x.VersionNo).Select(version =>
            new BeybladeVersionStatisticsRow(version.Id, version.VersionLabel, version.PartsSummary,
                BuildBeybladeSummary(userId, beybladeId, version.VersionLabel, rounds.Where(x => Version(x) == version.Id).ToList()), [])).ToList();
        var unknown = rounds.Where(x => Version(x) is null).ToList();
        if (unknown.Count > 0)
            rows.Add(new(null, "未記錄版本", "當時未記錄零件，保留於總戰績。",
                BuildBeybladeSummary(userId, beybladeId, "未記錄版本", unknown),
                [new("info", "這些小局沒有固定配置資料，因此不產生版本優缺點判讀。")]));

        var comparable = rows
            .Where(x => x.ConfigurationId.HasValue && x.Summary.RoundCount >= VersionInsightMinimumRounds)
            .ToList();
        var bestConfigurationId = comparable.Count < 2
            ? null
            : comparable.OrderByDescending(x => x.Summary.WinRate)
                .ThenByDescending(x => x.Summary.ScoreDifference)
                .ThenByDescending(x => x.Summary.RoundCount)
                .First().ConfigurationId;
        rows = rows.Select(row => row.ConfigurationId.HasValue
            ? row with { Insights = BuildVersionInsights(row.Summary, bestConfigurationId == row.ConfigurationId, comparable.Count >= 2) }
            : row).ToList();
        return new(blade.Name, blade.UpperName,
            BuildBeybladeSummary(userId, beybladeId, blade.Name, rounds), rows);
    }

    private static IReadOnlyList<BeybladePerformanceInsight> BuildVersionInsights(
        BeybladeStatisticsViewModel stats,
        bool isBestComparableVersion,
        bool hasComparableVersions)
    {
        if (stats.RoundCount < VersionInsightMinimumRounds)
        {
            var remaining = VersionInsightMinimumRounds - stats.RoundCount;
            return [new("info", $"目前 {stats.RoundCount} 局，再累積 {remaining} 局後顯示優缺點摘要。")];
        }

        var insights = new List<BeybladePerformanceInsight>();
        if (stats.Wins + stats.Losses >= VersionInsightMinimumRounds)
        {
            if (stats.WinRate >= .6m)
                insights.Add(new("success", $"優勢：目前勝率 {stats.WinRate:P0}。"));
            else if (stats.WinRate <= .4m)
                insights.Add(new("warning", $"留意：目前勝率 {stats.WinRate:P0}。"));
        }

        if (stats.ScoreDifference > 0)
            insights.Add(new("success", $"優勢：累計得失分差 +{stats.ScoreDifference}。"));
        else if (stats.ScoreDifference < 0)
            insights.Add(new("warning", $"留意：累計得失分差 {stats.ScoreDifference}。"));
        else
            insights.Add(new("info", "目前累計得分與失分持平。"));

        if (stats.BSide.Samples >= 3 && stats.XSide.Samples >= 3 &&
            Math.Abs(stats.BSide.WinRate - stats.XSide.WinRate) >= .2m)
        {
            var betterSide = stats.BSide.WinRate > stats.XSide.WinRate ? "B Side" : "X Side";
            var betterRate = Math.Max(stats.BSide.WinRate, stats.XSide.WinRate);
            var weakerSide = betterSide == "B Side" ? "X Side" : "B Side";
            var weakerRate = Math.Min(stats.BSide.WinRate, stats.XSide.WinRate);
            insights.Add(new("info", $"站位差異：{betterSide} 勝率 {betterRate:P0}，{weakerSide} 為 {weakerRate:P0}。"));
        }

        if (hasComparableVersions && isBestComparableVersion)
            insights.Add(new("success", "比較目前樣本達標的版本，這個版本勝率最高。"));

        return insights;
    }

    private static BeybladeStatisticsViewModel BuildBeybladeSummary(int userId, int beybladeId, string name, List<BattleRound> myRounds)
    {
        var effectiveEvents = myRounds.SelectMany(r => r.Events).Where(e => e.IsEffective).ToList();
        var resultEvents = effectiveEvents
            .Where(e => e.EventType == BattleRoundEventType.BattleResult && e.WinnerPlayerId.HasValue)
            .ToList();
        var wins = resultEvents.Count(e => e.WinnerPlayerId == userId);
        var losses = resultEvents.Count(e => e.WinnerPlayerId != userId);
        var score = effectiveEvents.Where(e => e.WinnerPlayerId == userId).Sum(e => e.ScoreAwarded);
        var against = effectiveEvents.Where(e => e.WinnerPlayerId.HasValue && e.WinnerPlayerId != userId).Sum(e => e.ScoreAwarded);
        var launchFaultEvents = effectiveEvents
            .Where(e => e.EventType == BattleRoundEventType.LaunchFaultPenalty && e.ActorPlayerId == userId)
            .ToList();

        return new BeybladeStatisticsViewModel(
            beybladeId,
            name,
            wins,
            losses,
            Rate(wins, losses),
            score,
            against,
            launchFaultEvents.Sum(e => e.ScoreAwarded),
            myRounds.Count,
            Average(score, myRounds.Count),
            Average(against, myRounds.Count),
            launchFaultEvents.Count,
            BuildResultTypeStatistics(resultEvents, userId),
            BuildRoundSideStatistics(myRounds, userId, BattleSide.B),
            BuildRoundSideStatistics(myRounds, userId, BattleSide.X));
    }

    public async Task<List<OpponentStatisticsViewModel>> GetOpponentStatisticsAsync(
        int userId,
        StatisticsSourceFilter source = StatisticsSourceFilter.Quick)
    {
        var rows = new List<OpponentStatisticsViewModel>();
        if (source is StatisticsSourceFilter.All or StatisticsSourceFilter.Quick)
        {
            rows.AddRange(await GetPlayerOpponentStatisticsAsync(userId, BattleSourceType.Quick));
        }

        if (source is StatisticsSourceFilter.All or StatisticsSourceFilter.TournamentIndividual)
        {
            rows.AddRange(await GetPlayerOpponentStatisticsAsync(userId, BattleSourceType.TournamentIndividual));
        }

        if (source is StatisticsSourceFilter.All or StatisticsSourceFilter.TournamentTeam)
        {
            rows.AddRange(await GetTeamRoundOpponentStatisticsAsync(userId));
        }

        return MergeOpponentStatistics(rows);
    }

    public async Task<List<OpponentBeybladeStatisticsViewModel>> GetOpponentBeybladeStatisticsAsync(
        int userId,
        int opponentId,
        StatisticsSourceFilter source = StatisticsSourceFilter.All)
    {
        var rounds = await ApplyRoundSourceFilter(ValidCompletedRounds(userId), source)
            .Include(x => x.Lineup)
            .Include(x => x.Events)
            .Where(x =>
                (x.PlayerAId == userId && x.PlayerBId == opponentId) ||
                (x.PlayerBId == userId && x.PlayerAId == opponentId))
            .ToListAsync();

        return rounds
            .GroupBy(round => round.PlayerAId == userId
                ? (round.PlayerABeybladeId, round.Lineup.PlayerAConfigurationId, round.PlayerBBeybladeId, round.Lineup.PlayerBConfigurationId)
                : (round.PlayerBBeybladeId, round.Lineup.PlayerBConfigurationId, round.PlayerABeybladeId, round.Lineup.PlayerAConfigurationId))
            .Select(group =>
            {
                var effectiveEvents = group.SelectMany(r => r.Events).Where(e => e.IsEffective).ToList();
                var resultEvents = effectiveEvents
                    .Where(e => e.EventType == BattleRoundEventType.BattleResult && e.WinnerPlayerId.HasValue)
                    .ToList();
                var wins = resultEvents.Count(e => e.WinnerPlayerId == userId);
                var losses = resultEvents.Count(e => e.WinnerPlayerId == opponentId);
                var score = effectiveEvents.Where(e => e.WinnerPlayerId == userId).Sum(e => e.ScoreAwarded);
                var against = effectiveEvents.Where(e => e.WinnerPlayerId == opponentId).Sum(e => e.ScoreAwarded);
                return new OpponentBeybladeStatisticsViewModel(
                    group.OrderByDescending(x => x.Id).Select(x => x.PlayerAId == userId ? x.PlayerABeybladeNameSnapshot : x.PlayerBBeybladeNameSnapshot).First(),
                    group.OrderByDescending(x => x.Id).Select(x => x.PlayerAId == userId ? x.PlayerBBeybladeNameSnapshot : x.PlayerABeybladeNameSnapshot).First(),
                    wins,
                    losses,
                    Rate(wins, losses),
                    score,
                    against);
            })
            .OrderByDescending(x => x.WinRate)
            .ThenByDescending(x => x.Score)
            .ThenBy(x => x.MyBeybladeName)
            .ThenBy(x => x.OpponentBeybladeName)
            .ToList();
    }

    public async Task<List<BattleHistoryViewModel>> GetBattleHistoryAsync(
        int userId,
        StatisticsSourceFilter source = StatisticsSourceFilter.Quick)
    {
        var rows = new List<BattleHistoryViewModel>();
        if (source is StatisticsSourceFilter.All or StatisticsSourceFilter.Quick)
        {
            rows.AddRange(await GetPlayerBattleHistoryAsync(userId, BattleSourceType.Quick));
        }

        if (source is StatisticsSourceFilter.All or StatisticsSourceFilter.TournamentIndividual)
        {
            rows.AddRange(await GetPlayerBattleHistoryAsync(userId, BattleSourceType.TournamentIndividual));
        }

        if (source is StatisticsSourceFilter.All or StatisticsSourceFilter.TournamentTeam)
        {
            rows.AddRange(await GetTeamBattleHistoryAsync(userId));
        }

        return rows
            .OrderByDescending(x => x.CompletedAtUtc)
            .ThenByDescending(x => x.BattleId)
            .ToList();
    }

    private async Task<UserSummaryViewModel> GetPlayerBattleSummaryAsync(
        int userId,
        BattleSourceType source,
        StatisticsSideFilter sideFilter)
    {
        var battles = await CompletedPlayerBattles(userId, source).ToListAsync();
        battles = battles.Where(x => MatchesSide(GetBattleSide(x, userId), sideFilter)).ToList();
        var wins = battles.Count(x => x.WinningPlayerId == userId);
        var losses = battles.Count(x => x.WinningPlayerId.HasValue && x.WinningPlayerId != userId);
        var score = battles.Sum(x => x.PlayerAId == userId ? x.SideAScore : x.SideBScore);
        var against = battles.Sum(x => x.PlayerAId == userId ? x.SideBScore : x.SideAScore);
        var faultLost = await GetLaunchFaultAgainstScoreAsync(userId, battles.Select(x => x.Id));

        return new UserSummaryViewModel(
            wins,
            losses,
            Rate(wins, losses),
            score,
            against,
            faultLost,
            BuildBattleSideStatistics(battles, userId, BattleSide.B),
            BuildBattleSideStatistics(battles, userId, BattleSide.X));
    }

    private async Task<UserSummaryViewModel> GetTeamResultSummaryAsync(
        int userId,
        StatisticsSideFilter sideFilter)
    {
        var battles = await db.Battles
            .Where(x =>
                (x.Status == BattleStatus.Completed || x.Status == BattleStatus.Forfeited) &&
                x.SourceType == BattleSourceType.TournamentTeam &&
                x.TournamentMatch != null &&
                x.TournamentMatch.Participants.Any(p => p.UserId == userId))
            .Include(x => x.TournamentMatch!)
                .ThenInclude(x => x.Participants)
            .ToListAsync();

        var usable = battles
            .Select(battle =>
            {
                var entryId = battle.TournamentMatch!.Participants
                    .First(p => p.UserId == userId)
                    .TournamentEntryId;
                var isSideA = battle.TournamentMatch.SideAEntryId == entryId;
                var side = isSideA
                    ? battle.SideADesignation
                    : Opposite(battle.SideADesignation);
                return new TeamBattleResult(battle, entryId, isSideA, side);
            })
            .Where(x =>
                x.Battle.TournamentMatch!.SideAEntryId == x.EntryId ||
                x.Battle.TournamentMatch.SideBEntryId == x.EntryId)
            .Where(x => MatchesSide(x.Side, sideFilter))
            .ToList();
        var wins = usable.Count(x => x.Battle.TournamentMatch!.WinnerEntryId == x.EntryId);
        var losses = usable.Count(x =>
            x.Battle.TournamentMatch!.WinnerEntryId.HasValue &&
            x.Battle.TournamentMatch.WinnerEntryId != x.EntryId);
        var score = usable.Sum(x => x.IsSideA ? x.Battle.SideAScore : x.Battle.SideBScore);
        var against = usable.Sum(x => x.IsSideA ? x.Battle.SideBScore : x.Battle.SideAScore);
        var faultLost = await GetLaunchFaultAgainstScoreAsync(userId, usable.Select(x => x.Battle.Id));

        return new UserSummaryViewModel(
            wins,
            losses,
            Rate(wins, losses),
            score,
            against,
            faultLost,
            BuildTeamBattleSideStatistics(usable, BattleSide.B),
            BuildTeamBattleSideStatistics(usable, BattleSide.X));
    }

    private async Task<UserSummaryViewModel> GetTeamRoundSummaryAsync(
        int userId,
        StatisticsSideFilter sideFilter)
    {
        var rounds = await ValidCompletedRounds(userId)
            .Where(x => x.Battle.SourceType == BattleSourceType.TournamentTeam)
            .Include(x => x.Battle)
            .Include(x => x.Events)
            .ToListAsync();
        rounds = rounds.Where(x => MatchesSide(GetRoundSide(x, userId), sideFilter)).ToList();
        return BuildRoundSummary(rounds, userId);
    }

    private async Task<int> GetLaunchFaultAgainstScoreAsync(int userId, IEnumerable<int> ids)
    {
        var battleIds = ids.Distinct().ToList();
        return battleIds.Count == 0
            ? 0
            : await db.BattleRoundEvents
                .Where(x =>
                    x.IsEffective &&
                    x.EventType == BattleRoundEventType.LaunchFaultPenalty &&
                    x.ActorPlayerId == userId &&
                    battleIds.Contains(x.BattleRound.BattleId))
                .SumAsync(x => (int?)x.ScoreAwarded) ?? 0;
    }

    private async Task<List<OpponentStatisticsViewModel>> GetPlayerOpponentStatisticsAsync(
        int userId,
        BattleSourceType source)
    {
        var battles = await CompletedPlayerBattles(userId, source)
            .Include(x => x.PlayerA)
            .Include(x => x.PlayerB)
            .ToListAsync();

        return battles
            .GroupBy(x => x.PlayerAId == userId ? x.PlayerB : x.PlayerA)
            .Where(group => group.Key is not null)
            .Select(group =>
            {
                var wins = group.Count(x => x.WinningPlayerId == userId);
                var losses = group.Count(x => x.WinningPlayerId.HasValue && x.WinningPlayerId != userId);
                var score = group.Sum(x => x.PlayerAId == userId ? x.SideAScore : x.SideBScore);
                var against = group.Sum(x => x.PlayerAId == userId ? x.SideBScore : x.SideAScore);
                return new OpponentStatisticsViewModel(
                    group.Key!.Id,
                    group.Key.DisplayName,
                    wins,
                    losses,
                    Rate(wins, losses),
                    score,
                    against);
            })
            .ToList();
    }

    private async Task<List<OpponentStatisticsViewModel>> GetTeamRoundOpponentStatisticsAsync(int userId)
    {
        var rounds = await ValidCompletedRounds(userId)
            .Where(x =>
                x.Battle.SourceType == BattleSourceType.TournamentTeam &&
                x.PlayerAId.HasValue &&
                x.PlayerBId.HasValue)
            .Include(x => x.Events)
            .ToListAsync();

        return rounds
            .GroupBy(round => round.PlayerAId == userId
                ? (Id: round.PlayerBId!.Value, Name: round.PlayerBDisplayNameSnapshot)
                : (Id: round.PlayerAId!.Value, Name: round.PlayerADisplayNameSnapshot))
            .Select(group =>
            {
                var effectiveEvents = group.SelectMany(x => x.Events).Where(x => x.IsEffective).ToList();
                var resultEvents = effectiveEvents
                    .Where(x => x.EventType == BattleRoundEventType.BattleResult && x.WinnerPlayerId.HasValue)
                    .ToList();
                var wins = resultEvents.Count(x => x.WinnerPlayerId == userId);
                var losses = resultEvents.Count(x => x.WinnerPlayerId != userId);
                var score = effectiveEvents.Where(x => x.WinnerPlayerId == userId).Sum(x => x.ScoreAwarded);
                var against = effectiveEvents.Where(x => x.WinnerPlayerId.HasValue && x.WinnerPlayerId != userId).Sum(x => x.ScoreAwarded);
                return new OpponentStatisticsViewModel(
                    group.Key.Id,
                    group.Key.Name,
                    wins,
                    losses,
                    Rate(wins, losses),
                    score,
                    against);
            })
            .ToList();
    }

    private async Task<List<BattleHistoryViewModel>> GetPlayerBattleHistoryAsync(
        int userId,
        BattleSourceType source)
    {
        var battles = await CompletedPlayerBattles(userId, source)
            .Include(x => x.PlayerA)
            .Include(x => x.PlayerB)
            .ToListAsync();
        return battles.Select(x => new BattleHistoryViewModel(
            x.Id,
            (x.PlayerAId == userId ? x.PlayerB : x.PlayerA)!.DisplayName,
            x.PlayerAId == userId ? x.SideAScore : x.SideBScore,
            x.PlayerAId == userId ? x.SideBScore : x.SideAScore,
            x.WinningPlayerId == userId,
            x.CompletedAtUtc,
            x.SourceType,
            false,
            GetBattleSide(x, userId))).ToList();
    }

    private async Task<List<BattleHistoryViewModel>> GetTeamBattleHistoryAsync(int userId)
    {
        var battles = await db.Battles
            .Where(x =>
                (x.Status == BattleStatus.Completed || x.Status == BattleStatus.Forfeited) &&
                x.SourceType == BattleSourceType.TournamentTeam &&
                x.TournamentMatch != null &&
                x.TournamentMatch.Participants.Any(p => p.UserId == userId))
            .Include(x => x.TournamentMatch!)
                .ThenInclude(x => x.Participants)
            .Include(x => x.TournamentMatch!)
                .ThenInclude(x => x.SideAEntry)
            .Include(x => x.TournamentMatch!)
                .ThenInclude(x => x.SideBEntry)
            .ToListAsync();

        var rows = new List<BattleHistoryViewModel>();
        foreach (var battle in battles)
        {
            var match = battle.TournamentMatch!;
            var entryId = match.Participants.First(x => x.UserId == userId).TournamentEntryId;
            var isSideA = match.SideAEntryId == entryId;
            if (!isSideA && match.SideBEntryId != entryId)
            {
                continue;
            }

            var opponentName = isSideA
                ? match.SideBEntry?.DisplayNameSnapshot ?? "對手隊伍"
                : match.SideAEntry?.DisplayNameSnapshot ?? "對手隊伍";
            rows.Add(new BattleHistoryViewModel(
                battle.Id,
                opponentName,
                isSideA ? battle.SideAScore : battle.SideBScore,
                isSideA ? battle.SideBScore : battle.SideAScore,
                match.WinnerEntryId == entryId,
                battle.CompletedAtUtc,
                BattleSourceType.TournamentTeam,
                true,
                isSideA ? battle.SideADesignation : Opposite(battle.SideADesignation)));
        }

        return rows;
    }

    private IQueryable<Battle> CompletedPlayerBattles(int userId, BattleSourceType source) =>
        db.Battles.Where(x =>
            (x.Status == BattleStatus.Completed || x.Status == BattleStatus.Forfeited) &&
            x.SourceType == source &&
            (x.PlayerAId == userId || x.PlayerBId == userId));

    private IQueryable<BattleRound> ValidCompletedRounds(int userId) =>
        db.BattleRounds.Where(x =>
            x.Status == BattleRoundStatus.Completed &&
            (x.PlayerAId == userId || x.PlayerBId == userId) &&
            (x.Battle.Status == BattleStatus.Completed ||
             x.Battle.Status == BattleStatus.Forfeited ||
             (x.Battle.SourceType != BattleSourceType.Quick && x.Battle.Status == BattleStatus.Cancelled)));

    private static IQueryable<BattleRound> ApplyRoundSourceFilter(
        IQueryable<BattleRound> query,
        StatisticsSourceFilter source) => source switch
        {
            StatisticsSourceFilter.Quick => query.Where(x => x.Battle.SourceType == BattleSourceType.Quick),
            StatisticsSourceFilter.TournamentIndividual => query.Where(x => x.Battle.SourceType == BattleSourceType.TournamentIndividual),
            StatisticsSourceFilter.TournamentTeam => query.Where(x => x.Battle.SourceType == BattleSourceType.TournamentTeam),
            _ => query
        };

    private static UserSummaryViewModel BuildRoundSummary(List<BattleRound> rounds, int userId)
    {
        var effectiveEvents = rounds.SelectMany(x => x.Events).Where(x => x.IsEffective).ToList();
        var resultEvents = effectiveEvents
            .Where(x => x.EventType == BattleRoundEventType.BattleResult && x.WinnerPlayerId.HasValue)
            .ToList();
        var wins = resultEvents.Count(x => x.WinnerPlayerId == userId);
        var losses = resultEvents.Count(x => x.WinnerPlayerId != userId);
        var score = effectiveEvents.Where(x => x.WinnerPlayerId == userId).Sum(x => x.ScoreAwarded);
        var against = effectiveEvents.Where(x => x.WinnerPlayerId.HasValue && x.WinnerPlayerId != userId).Sum(x => x.ScoreAwarded);
        var faultLost = effectiveEvents
            .Where(x => x.EventType == BattleRoundEventType.LaunchFaultPenalty && x.ActorPlayerId == userId)
            .Sum(x => x.ScoreAwarded);
        return new UserSummaryViewModel(
            wins,
            losses,
            Rate(wins, losses),
            score,
            against,
            faultLost,
            BuildRoundSideStatistics(rounds, userId, BattleSide.B),
            BuildRoundSideStatistics(rounds, userId, BattleSide.X));
    }

    private static SideStatisticsViewModel BuildBattleSideStatistics(
        IEnumerable<Battle> battles,
        int userId,
        BattleSide side)
    {
        var selected = battles.Where(x => GetBattleSide(x, userId) == side).ToList();
        var wins = selected.Count(x => x.WinningPlayerId == userId);
        var losses = selected.Count(x => x.WinningPlayerId.HasValue && x.WinningPlayerId != userId);
        return new SideStatisticsViewModel(wins, losses, Rate(wins, losses));
    }

    private static SideStatisticsViewModel BuildTeamBattleSideStatistics(
        IEnumerable<TeamBattleResult> battles,
        BattleSide side)
    {
        var selected = battles.Where(x => x.Side == side).ToList();
        var wins = selected.Count(x => x.Battle.TournamentMatch!.WinnerEntryId == x.EntryId);
        var losses = selected.Count(x =>
            x.Battle.TournamentMatch!.WinnerEntryId.HasValue &&
            x.Battle.TournamentMatch.WinnerEntryId != x.EntryId);
        return new SideStatisticsViewModel(wins, losses, Rate(wins, losses));
    }

    private static SideStatisticsViewModel BuildRoundSideStatistics(
        IEnumerable<BattleRound> rounds,
        int userId,
        BattleSide side)
    {
        var resultEvents = rounds
            .Where(x => GetRoundSide(x, userId) == side)
            .SelectMany(x => x.Events)
            .Where(x =>
                x.IsEffective &&
                x.EventType == BattleRoundEventType.BattleResult &&
                x.WinnerPlayerId.HasValue)
            .ToList();
        var wins = resultEvents.Count(x => x.WinnerPlayerId == userId);
        var losses = resultEvents.Count(x => x.WinnerPlayerId != userId);
        return new SideStatisticsViewModel(wins, losses, Rate(wins, losses));
    }

    private static BattleSide? GetBattleSide(Battle battle, int userId)
    {
        if (battle.SideADesignation is null)
        {
            return null;
        }

        return battle.PlayerAId == userId
            ? battle.SideADesignation
            : Opposite(battle.SideADesignation);
    }

    private static BattleSide? GetRoundSide(BattleRound round, int userId)
    {
        if (round.Battle.SideADesignation is null)
        {
            return null;
        }

        return round.PlayerAId == userId
            ? round.Battle.SideADesignation
            : Opposite(round.Battle.SideADesignation);
    }

    private static BattleSide? Opposite(BattleSide? side) => side switch
    {
        BattleSide.B => BattleSide.X,
        BattleSide.X => BattleSide.B,
        _ => null
    };

    private static bool MatchesSide(BattleSide? side, StatisticsSideFilter filter) => filter switch
    {
        StatisticsSideFilter.B => side == BattleSide.B,
        StatisticsSideFilter.X => side == BattleSide.X,
        _ => true
    };

    private static List<OpponentStatisticsViewModel> MergeOpponentStatistics(
        IEnumerable<OpponentStatisticsViewModel> rows) => rows
        .GroupBy(x => x.OpponentId)
        .Select(group =>
        {
            var wins = group.Sum(x => x.Wins);
            var losses = group.Sum(x => x.Losses);
            return new OpponentStatisticsViewModel(
                group.Key,
                group.First().DisplayName,
                wins,
                losses,
                Rate(wins, losses),
                group.Sum(x => x.Score),
                group.Sum(x => x.AgainstScore));
        })
        .OrderByDescending(x => x.WinRate)
        .ThenByDescending(x => x.Score)
        .ThenBy(x => x.DisplayName)
        .ToList();

    private static ResultTypeStatisticsViewModel BuildResultTypeStatistics(
        IEnumerable<BattleRoundEvent> resultEvents,
        int userId)
    {
        var events = resultEvents.ToList();
        int For(ResultType resultType) => events.Count(x => x.WinnerPlayerId == userId && x.ResultType == resultType);
        int Against(ResultType resultType) => events.Count(x => x.WinnerPlayerId != userId && x.ResultType == resultType);
        return new ResultTypeStatisticsViewModel(
            For(ResultType.SpinFinish),
            For(ResultType.KnockOut),
            For(ResultType.Burst),
            For(ResultType.Extreme),
            Against(ResultType.SpinFinish),
            Against(ResultType.KnockOut),
            Against(ResultType.Burst),
            Against(ResultType.Extreme));
    }

    private static decimal Rate(int wins, int losses) =>
        wins + losses == 0 ? 0 : Math.Round((decimal)wins / (wins + losses), 3);

    private static decimal Average(int total, int sampleCount) =>
        sampleCount == 0 ? 0 : Math.Round((decimal)total / sampleCount, 2);

    private static IEnumerable<UserStatisticsRowViewModel> SortUserStatistics(
        IEnumerable<UserStatisticsRowViewModel> source,
        string? sort) => sort switch
        {
            "score-desc" => source.OrderByDescending(x => x.Summary.Score).ThenBy(x => x.Label),
            "score-asc" => source.OrderBy(x => x.Summary.Score).ThenBy(x => x.Label),
            "against-desc" => source.OrderByDescending(x => x.Summary.AgainstScore).ThenBy(x => x.Label),
            "against-asc" => source.OrderBy(x => x.Summary.AgainstScore).ThenBy(x => x.Label),
            "difference-desc" => source.OrderByDescending(x => x.Summary.ScoreDifference).ThenBy(x => x.Label),
            "difference-asc" => source.OrderBy(x => x.Summary.ScoreDifference).ThenBy(x => x.Label),
            "b-winrate-desc" => source.OrderByDescending(x => x.Summary.BSide.WinRate).ThenByDescending(x => x.Summary.BSide.Samples).ThenBy(x => x.Label),
            "b-winrate-asc" => source.OrderBy(x => x.Summary.BSide.WinRate).ThenByDescending(x => x.Summary.BSide.Samples).ThenBy(x => x.Label),
            "x-winrate-desc" => source.OrderByDescending(x => x.Summary.XSide.WinRate).ThenByDescending(x => x.Summary.XSide.Samples).ThenBy(x => x.Label),
            "x-winrate-asc" => source.OrderBy(x => x.Summary.XSide.WinRate).ThenByDescending(x => x.Summary.XSide.Samples).ThenBy(x => x.Label),
            "winrate-asc" => source.OrderBy(x => x.Summary.WinRate).ThenByDescending(x => x.Summary.Wins + x.Summary.Losses).ThenBy(x => x.Label),
            _ => source.OrderByDescending(x => x.Summary.WinRate).ThenByDescending(x => x.Summary.Wins + x.Summary.Losses).ThenBy(x => x.Label)
        };

    private static IEnumerable<BeybladeStatisticsViewModel> SortBeyblades(
        IEnumerable<BeybladeStatisticsViewModel> source,
        string? sort) => sort switch
        {
            "score-asc" => source.OrderBy(x => x.Score).ThenBy(x => x.Name),
            "against-desc" => source.OrderByDescending(x => x.AgainstScore).ThenBy(x => x.Name),
            "against-asc" => source.OrderBy(x => x.AgainstScore).ThenBy(x => x.Name),
            "difference-desc" => source.OrderByDescending(x => x.ScoreDifference).ThenBy(x => x.Name),
            "difference-asc" => source.OrderBy(x => x.ScoreDifference).ThenBy(x => x.Name),
            "b-winrate-desc" => source.OrderByDescending(x => x.BSide.WinRate).ThenByDescending(x => x.BSide.Samples).ThenBy(x => x.Name),
            "b-winrate-asc" => source.OrderBy(x => x.BSide.WinRate).ThenByDescending(x => x.BSide.Samples).ThenBy(x => x.Name),
            "x-winrate-desc" => source.OrderByDescending(x => x.XSide.WinRate).ThenByDescending(x => x.XSide.Samples).ThenBy(x => x.Name),
            "x-winrate-asc" => source.OrderBy(x => x.XSide.WinRate).ThenByDescending(x => x.XSide.Samples).ThenBy(x => x.Name),
            "winrate-desc" => source.OrderByDescending(x => x.WinRate).ThenByDescending(x => x.Wins).ThenBy(x => x.Name),
            "winrate-asc" => source.OrderBy(x => x.WinRate).ThenBy(x => x.Name),
            _ => source.OrderByDescending(x => x.Score).ThenBy(x => x.Name)
        };

    private sealed record TeamBattleResult(Battle Battle, int EntryId, bool IsSideA, BattleSide? Side);
}
