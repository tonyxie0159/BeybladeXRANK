using BeybladeRecordSystem.Domain;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Tests;

public class BattleRulesTests
{
    [Theory]
    [InlineData(ResultType.SpinFinish, 1)]
    [InlineData(ResultType.KnockOut, 2)]
    [InlineData(ResultType.Burst, 2)]
    [InlineData(ResultType.Extreme, 3)]
    public void ScoreFor_UsesSpecifiedServerSideScore(ResultType resultType, int expectedScore) =>
        Assert.Equal(expectedScore, BattleRules.ScoreFor(resultType));

    [Theory]
    [InlineData(3, 3, BattleStatus.InProgress)]
    [InlineData(4, 0, BattleStatus.VictoryPendingCompletion)]
    [InlineData(0, 4, BattleStatus.VictoryPendingCompletion)]
    public void StatusForScore_RequiresFourPoints(int aScore, int bScore, BattleStatus expected) =>
        Assert.Equal(expected, BattleRules.StatusForScore(aScore, bScore));

    [Theory]
    [InlineData(5, 4, 6, BattleStatus.InProgress)]
    [InlineData(6, 4, 6, BattleStatus.VictoryPendingCompletion)]
    [InlineData(7, 8, 8, BattleStatus.VictoryPendingCompletion)]
    public void StatusForScore_UsesBattleSpecificThreshold(int aScore, int bScore, int scoreToWin, BattleStatus expected) =>
        Assert.Equal(expected, BattleRules.StatusForScore(aScore, bScore, scoreToWin));

    [Fact]
    public void FaultCount_SecondPenaltyResetsOnlyFaultyBladeCounter()
    {
        var events = new[]
        {
            new BattleRoundEvent { EventSequence = 1, EventType = BattleRoundEventType.LaunchFault, ActorPlayerId = 10, IsEffective = true },
            new BattleRoundEvent { EventSequence = 2, EventType = BattleRoundEventType.LaunchFault, ActorPlayerId = 10, IsEffective = true },
            new BattleRoundEvent { EventSequence = 3, EventType = BattleRoundEventType.LaunchFaultPenalty, ActorPlayerId = 10, WinnerPlayerId = 20, ScoreAwarded = 1, IsEffective = true },
            new BattleRoundEvent { EventSequence = 4, EventType = BattleRoundEventType.LaunchFault, ActorPlayerId = 20, IsEffective = true }
        };

        Assert.Equal(0, BattleRules.FaultCount(events, 10));
        Assert.Equal(1, BattleRules.FaultCount(events, 20));
    }

    [Fact]
    public async Task LaunchFaultTwice_AwardsOpponentOnePoint_AndKeepsRoundOpen()
    {
        await using var setup = await TestBattle.CreateAsync();

        Assert.True((await setup.Service.RecordLaunchFaultAsync(setup.BattleId, setup.CurrentRoundId, setup.PlayerAId, setup.PlayerAId)).Succeeded);
        Assert.True((await setup.Service.RecordLaunchFaultAsync(setup.BattleId, setup.CurrentRoundId, setup.PlayerAId, setup.PlayerAId)).Succeeded);

        var round = await setup.Db.BattleRounds.Include(x => x.Events).SingleAsync(x => x.Id == setup.CurrentRoundId);
        var battle = await setup.Db.Battles.SingleAsync(x => x.Id == setup.BattleId);
        Assert.Equal(BattleRoundStatus.InProgress, round.Status);
        Assert.Equal(3, round.Events.Count);
        Assert.Contains(round.Events, x => x.EventType == BattleRoundEventType.LaunchFaultPenalty && x.WinnerPlayerId == setup.PlayerBId && x.ScoreAwarded == 1);
        Assert.Equal(0, battle.PlayerAScore);
        Assert.Equal(1, battle.PlayerBScore);
    }

    [Fact]
    public async Task ReviseRound_ReplacesEffectiveResult_RecalculatesScore_AndWritesAudit()
    {
        await using var setup = await TestBattle.CreateAsync();
        Assert.True((await setup.Service.RecordBattleResultAsync(setup.BattleId, setup.CurrentRoundId, setup.PlayerAId, setup.PlayerAId, ResultType.SpinFinish)).Succeeded);
        Assert.True((await setup.Service.CompleteRoundAsync(setup.BattleId, setup.CurrentRoundId, setup.PlayerAId)).Succeeded);

        var revision = await setup.Service.ReviseRoundAsync(setup.BattleId, setup.CurrentRoundId, setup.PlayerAId, setup.PlayerBId, ResultType.Burst, "判決修正");

        Assert.True(revision.Succeeded);
        var battle = await setup.Db.Battles.SingleAsync(x => x.Id == setup.BattleId);
        var events = await setup.Db.BattleRoundEvents.Where(x => x.BattleRoundId == setup.CurrentRoundId).OrderBy(x => x.EventSequence).ToListAsync();
        Assert.Equal(0, battle.PlayerAScore);
        Assert.Equal(2, battle.PlayerBScore);
        Assert.Single(events, x => x.IsEffective && x.EventType == BattleRoundEventType.BattleResult);
        Assert.Single(await setup.Db.BattleRoundRevisions.Where(x => x.BattleRoundId == setup.CurrentRoundId).ToListAsync());
    }

    [Fact]
    public async Task RevisingPreviousRound_ReusesImmediateUnscoredRoundWithoutCreatingGap()
    {
        await using var setup = await TestBattle.CreateAsync();
        Assert.True((await setup.Service.RecordBattleResultAsync(
            setup.BattleId, setup.CurrentRoundId, setup.PlayerAId, setup.PlayerAId, ResultType.Extreme)).Succeeded);
        var secondRound = (await setup.Service.CompleteRoundAsync(
            setup.BattleId, setup.CurrentRoundId, setup.PlayerAId)).Value!;

        Assert.True((await setup.Service.ReviseRoundAsync(
            setup.BattleId, setup.CurrentRoundId, setup.PlayerAId, setup.PlayerAId,
            ResultType.KnockOut, "首局由極限改為撞出")).Succeeded);

        setup.Db.ChangeTracker.Clear();
        var rounds = await setup.Db.BattleRounds.Include(x => x.Events)
            .Where(x => x.BattleId == setup.BattleId)
            .OrderBy(x => x.RoundNo)
            .ToListAsync();
        Assert.Equal(2, rounds.Count);
        Assert.Equal(secondRound.Id, rounds[1].Id);
        Assert.Equal(2, rounds[1].RoundNo);
        Assert.Equal(BattleRoundStatus.InProgress, rounds[1].Status);
        Assert.Null(rounds[1].CompletedAtUtc);
        Assert.Empty(rounds[1].Events);
    }

    [Fact]
    public async Task RevisionOfEarlierRound_InvalidatesAllLaterRounds_AndRestartsAtNextPosition()
    {
        await using var setup = await TestBattle.CreateAsync();
        var roundIds = new List<int>();
        var roundId = setup.CurrentRoundId;
        for (var index = 0; index < 3; index++)
        {
            roundIds.Add(roundId);
            Assert.True((await setup.Service.RecordBattleResultAsync(
                setup.BattleId, roundId, setup.PlayerAId, setup.PlayerAId, ResultType.SpinFinish)).Succeeded);
            var completed = await setup.Service.CompleteRoundAsync(setup.BattleId, roundId, setup.PlayerAId);
            if (index < 2) roundId = completed.Value!.Id;
        }
        Assert.True((await setup.Flow.SubmitReorderAsync(
            setup.BattleId, setup.PlayerAId,
            setup.PlayerABladeIds.AsEnumerable().Reverse().ToList())).Succeeded);
        Assert.True((await setup.Flow.SubmitReorderAsync(
            setup.BattleId, setup.PlayerBId,
            setup.PlayerBBladeIds.AsEnumerable().Reverse().ToList())).Succeeded);
        var fourthRound = await setup.Db.BattleRounds.SingleAsync(x =>
            x.BattleId == setup.BattleId && x.RoundNo == 4);
        roundIds.Add(fourthRound.Id);
        Assert.True((await setup.Service.RecordBattleResultAsync(
            setup.BattleId, fourthRound.Id, setup.PlayerAId, setup.PlayerBId, ResultType.SpinFinish)).Succeeded);
        Assert.True((await setup.Service.CompleteRoundAsync(
            setup.BattleId, fourthRound.Id, setup.PlayerAId)).Succeeded);

        Assert.True((await setup.Service.ReviseRoundAsync(
            setup.BattleId, roundIds[0], setup.PlayerAId, setup.PlayerAId,
            ResultType.Extreme, "較早 Round 應為極限得分")).Succeeded);

        setup.Db.ChangeTracker.Clear();
        var battle = await setup.Db.Battles.SingleAsync(x => x.Id == setup.BattleId);
        var rounds = await setup.Db.BattleRounds.Include(x => x.Events)
            .Where(x => x.BattleId == setup.BattleId).OrderBy(x => x.RoundNo).ToListAsync();
        Assert.Equal(BattleStatus.InProgress, battle.Status);
        Assert.Equal(3, battle.SideAScore);
        Assert.Equal(0, battle.SideBScore);
        Assert.Contains(rounds[0].Events, x => !x.IsEffective && x.InvalidationReason == BattleRoundEventInvalidationReason.SupersededByRevision);
        Assert.Contains(rounds[0].Events, x => x.IsEffective && x.ResultType == ResultType.Extreme);
        Assert.Contains(rounds[2].Events, x => !x.IsEffective && x.InvalidationReason == BattleRoundEventInvalidationReason.SupersededByEarlierRoundRevision);
        Assert.Contains(rounds[3].Events, x => !x.IsEffective && x.InvalidationReason == BattleRoundEventInvalidationReason.SupersededByEarlierRoundRevision);
        Assert.Contains(rounds, x => x.RoundNo > 4 && x.PositionNo == 2 && x.Status == BattleRoundStatus.InProgress);

        Assert.True((await setup.Service.ReviseRoundAsync(
            setup.BattleId, roundIds[0], setup.PlayerAId, setup.PlayerAId,
            ResultType.SpinFinish, "恢復原始一分判決")).Succeeded);

        setup.Db.ChangeTracker.Clear();
        battle = await setup.Db.Battles.SingleAsync(x => x.Id == setup.BattleId);
        rounds = await setup.Db.BattleRounds.Include(x => x.Events)
            .Where(x => x.BattleId == setup.BattleId).OrderBy(x => x.RoundNo).ToListAsync();
        Assert.Equal(BattleStatus.InProgress, battle.Status);
        Assert.Equal(1, battle.SideAScore);
        Assert.Equal(0, battle.SideBScore);
        Assert.Contains(rounds[2].Events, x => !x.IsEffective && x.InvalidationReason == BattleRoundEventInvalidationReason.SupersededByEarlierRoundRevision);
        Assert.Contains(rounds[3].Events, x => !x.IsEffective && x.InvalidationReason == BattleRoundEventInvalidationReason.SupersededByEarlierRoundRevision);
        Assert.Single(rounds, x => x.Status == BattleRoundStatus.InProgress && x.PositionNo == 2);
        var revisions = await setup.Db.BattleRoundRevisions
            .Where(x => x.BattleRoundId == roundIds[0]).OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(2, revisions.Count);
        Assert.All(revisions, x =>
        {
            Assert.NotEmpty(x.PreviousBattleSnapshot);
            Assert.NotEmpty(x.NewBattleSnapshot);
        });
    }

    [Fact]
    public async Task RecordAndCompleteRound_RecordsResultAndCreatesNextRoundAtomically()
    {
        await using var setup = await TestBattle.CreateAsync();

        var result = await setup.Service.RecordAndCompleteRoundAsync(
            setup.BattleId, setup.CurrentRoundId, setup.PlayerAId, setup.PlayerAId, ResultType.SpinFinish);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        var rounds = await setup.Db.BattleRounds.Include(x => x.Events)
            .Where(x => x.BattleId == setup.BattleId).OrderBy(x => x.RoundNo).ToListAsync();
        Assert.Equal(BattleRoundStatus.Completed, rounds[0].Status);
        Assert.Single(rounds[0].Events, x => x.IsEffective && x.EventType == BattleRoundEventType.BattleResult);
        Assert.Equal(BattleRoundStatus.InProgress, rounds[1].Status);
        Assert.Equal(2, rounds[1].PositionNo);
    }

    [Fact]
    public async Task RevisingCompletedBattleBelowThreshold_ReopensBattleAtNextUnplayedPosition()
    {
        await using var setup = await TestBattle.CreateAsync();
        Assert.True((await setup.Service.RecordBattleResultAsync(
            setup.BattleId, setup.CurrentRoundId, setup.PlayerAId, setup.PlayerAId, ResultType.Extreme)).Succeeded);
        var secondRound = (await setup.Service.CompleteRoundAsync(
            setup.BattleId, setup.CurrentRoundId, setup.PlayerAId)).Value!;
        Assert.True((await setup.Service.RecordBattleResultAsync(
            setup.BattleId, secondRound.Id, setup.PlayerAId, setup.PlayerAId, ResultType.SpinFinish)).Succeeded);
        Assert.True((await setup.Service.FinishBattleAsync(setup.BattleId, setup.PlayerAId)).Succeeded);

        Assert.True((await setup.Service.ReviseRoundAsync(
            setup.BattleId, secondRound.Id, setup.PlayerAId, setup.PlayerBId,
            ResultType.SpinFinish, "第二局勝方修正")).Succeeded);

        setup.Db.ChangeTracker.Clear();
        var battle = await setup.Db.Battles.Include(x => x.Rounds).SingleAsync(x => x.Id == setup.BattleId);
        Assert.Equal(BattleStatus.InProgress, battle.Status);
        Assert.Equal(3, battle.SideAScore);
        Assert.Equal(1, battle.SideBScore);
        Assert.Null(battle.WinningPlayerId);
        Assert.Null(battle.WinningSide);
        Assert.Null(battle.CompletedAtUtc);
        var continuation = Assert.Single(battle.Rounds, x => x.Status == BattleRoundStatus.InProgress);
        Assert.Equal(3, continuation.RoundNo);
        Assert.Equal(3, continuation.PositionNo);
    }

    [Fact]
    public async Task ThreeCompletedRoundsBelowFour_CanReorderOnlyOriginalBlades_AndKeepsScore()
    {
        await using var setup = await TestBattle.CreateAsync();
        var roundId = setup.CurrentRoundId;
        for (var index = 0; index < 3; index++)
        {
            Assert.True((await setup.Service.RecordBattleResultAsync(setup.BattleId, roundId, setup.PlayerAId, setup.PlayerAId, ResultType.SpinFinish)).Succeeded);
            var complete = await setup.Service.CompleteRoundAsync(setup.BattleId, roundId, setup.PlayerAId);
            if (index < 2) roundId = complete.Value!.Id;
        }

        Assert.True((await setup.Flow.SubmitReorderAsync(
            setup.BattleId, setup.PlayerAId,
            setup.PlayerABladeIds.AsEnumerable().Reverse().ToList())).Succeeded);
        Assert.True((await setup.Flow.SubmitReorderAsync(
            setup.BattleId, setup.PlayerBId,
            setup.PlayerBBladeIds.AsEnumerable().Reverse().ToList())).Succeeded);

        var battle = await setup.Db.Battles.SingleAsync(x => x.Id == setup.BattleId);
        var currentLineup = await setup.Db.BattleLineups.Where(x => x.BattleId == setup.BattleId && x.IsCurrent).OrderBy(x => x.PositionNo).ToListAsync();
        var fourthRound = await setup.Db.BattleRounds.SingleAsync(x => x.BattleId == setup.BattleId && x.RoundNo == 4);
        Assert.Equal(3, battle.PlayerAScore);
        Assert.Equal(2, currentLineup[0].SequenceNo);
        Assert.Equal(setup.PlayerABladeIds[2], currentLineup[0].PlayerABeybladeId);
        Assert.Equal(4, fourthRound.RoundNo);
    }

    [Fact]
    public async Task FourPoints_RequiresExplicitFinishToCompleteBattle()
    {
        await using var setup = await TestBattle.CreateAsync();
        Assert.True((await setup.Service.RecordBattleResultAsync(setup.BattleId, setup.CurrentRoundId, setup.PlayerAId, setup.PlayerAId, ResultType.Extreme)).Succeeded);
        var secondRound = (await setup.Service.CompleteRoundAsync(setup.BattleId, setup.CurrentRoundId, setup.PlayerAId)).Value!;
        Assert.True((await setup.Service.RecordBattleResultAsync(setup.BattleId, secondRound.Id, setup.PlayerAId, setup.PlayerAId, ResultType.SpinFinish)).Succeeded);
        var beforeFinish = await setup.Db.Battles.SingleAsync(x => x.Id == setup.BattleId);
        Assert.Equal(BattleStatus.VictoryPendingCompletion, beforeFinish.Status);

        Assert.True((await setup.Service.FinishBattleAsync(setup.BattleId, setup.PlayerAId)).Succeeded);
        var completed = await setup.Db.Battles.SingleAsync(x => x.Id == setup.BattleId);
        Assert.Equal(BattleStatus.Completed, completed.Status);
        Assert.Equal(setup.PlayerAId, completed.WinningPlayerId);
        Assert.Equal(BattleSide.B, completed.WinningSide);
    }

    [Fact]
    public async Task QuickBattle_KeepsLegacyRulesAndStoresActualPlayersInLineupAndRound()
    {
        await using var setup = await TestBattle.CreateAsync();

        var battle = await setup.Db.Battles.SingleAsync(x => x.Id == setup.BattleId);
        var lineup = await setup.Db.BattleLineups
            .Where(x => x.BattleId == setup.BattleId)
            .OrderBy(x => x.PositionNo)
            .ToListAsync();
        var round = await setup.Db.BattleRounds.SingleAsync(x => x.Id == setup.CurrentRoundId);

        Assert.Equal(BattleSourceType.Quick, battle.SourceType);
        Assert.Equal(4, battle.ScoreToWin);
        Assert.Equal(BattleSide.B, battle.SideADesignation);
        Assert.Null(battle.TournamentMatchId);
        Assert.All(lineup, item =>
        {
            Assert.Equal(setup.PlayerAId, item.PlayerAId);
            Assert.Equal("A", item.PlayerADisplayNameSnapshot);
            Assert.Equal(setup.PlayerBId, item.PlayerBId);
            Assert.Equal("B", item.PlayerBDisplayNameSnapshot);
        });
        Assert.Equal(setup.PlayerAId, round.PlayerAId);
        Assert.Equal("A", round.PlayerADisplayNameSnapshot);
        Assert.Equal(setup.PlayerBId, round.PlayerBId);
        Assert.Equal("B", round.PlayerBDisplayNameSnapshot);
    }

    [Fact]
    public async Task QuickBattleForfeit_PreservesCompletedRound_InvalidatesCurrentRound_AndCountsStatistics()
    {
        await using var setup = await TestBattle.CreateAsync();
        Assert.True((await setup.Service.RecordBattleResultAsync(
            setup.BattleId, setup.CurrentRoundId, setup.PlayerAId,
            setup.PlayerAId, ResultType.SpinFinish)).Succeeded);
        var secondRound = (await setup.Service.CompleteRoundAsync(
            setup.BattleId, setup.CurrentRoundId, setup.PlayerAId)).Value!;
        Assert.True((await setup.Service.RecordLaunchFaultAsync(
            setup.BattleId, secondRound.Id, setup.PlayerAId, setup.PlayerAId)).Succeeded);
        Assert.True((await setup.Service.RecordLaunchFaultAsync(
            setup.BattleId, secondRound.Id, setup.PlayerAId, setup.PlayerAId)).Succeeded);

        Assert.False((await setup.Service.ForfeitQuickBattleAsync(
            setup.BattleId, setup.PlayerBId, setup.PlayerBId)).Succeeded);
        Assert.False((await setup.Service.ForfeitQuickBattleAsync(
            setup.BattleId, setup.PlayerAId, int.MaxValue)).Succeeded);
        Assert.True((await setup.Service.ForfeitQuickBattleAsync(
            setup.BattleId, setup.PlayerAId, setup.PlayerBId)).Succeeded);

        setup.Db.ChangeTracker.Clear();
        var battle = await setup.Db.Battles.SingleAsync(x => x.Id == setup.BattleId);
        var rounds = await setup.Db.BattleRounds.Include(x => x.Events)
            .Where(x => x.BattleId == setup.BattleId).OrderBy(x => x.RoundNo).ToListAsync();
        Assert.Equal(BattleStatus.Forfeited, battle.Status);
        Assert.Equal(setup.PlayerAId, battle.WinningPlayerId);
        Assert.Equal(BattleSide.B, battle.WinningSide);
        Assert.Equal(1, battle.SideAScore);
        Assert.Equal(0, battle.SideBScore);
        Assert.NotNull(battle.CompletedAtUtc);
        Assert.All(rounds[0].Events, x => Assert.True(x.IsEffective));
        Assert.All(rounds[1].Events, x =>
        {
            Assert.False(x.IsEffective);
            Assert.Equal(BattleRoundEventInvalidationReason.BattleTerminated, x.InvalidationReason);
        });

        var statistics = new StatisticsService(setup.Db);
        var winner = await statistics.GetUserSummaryAsync(setup.PlayerAId);
        var loser = await statistics.GetUserSummaryAsync(setup.PlayerBId);
        Assert.Equal((1, 0, 1, 0), (winner.Wins, winner.Losses, winner.Score, winner.AgainstScore));
        Assert.Equal((0, 1, 0, 1), (loser.Wins, loser.Losses, loser.Score, loser.AgainstScore));
        var winnerBlade = (await statistics.GetBeybladeStatisticsAsync(setup.PlayerAId, null))
            .Single(x => x.BeybladeId == setup.PlayerABladeIds[0]);
        Assert.Equal((1, 0, 1, 0), (winnerBlade.Wins, winnerBlade.Losses, winnerBlade.Score, winnerBlade.AgainstScore));
        Assert.True((await statistics.GetBattleHistoryAsync(setup.PlayerAId)).Single().Won);
        Assert.Equal(1, (await statistics.GetOpponentStatisticsAsync(setup.PlayerAId)).Single().Wins);
        Assert.False((await setup.Service.ReviseRoundAsync(
            setup.BattleId, setup.CurrentRoundId, setup.PlayerAId,
            setup.PlayerBId, ResultType.Extreme, "終止後不可修改")).Succeeded);
    }

    [Fact]
    public async Task QuickBattleCancellation_RequiresConfirmationAndHardDeletesEntireAggregate()
    {
        await using var setup = await TestBattle.CreateAsync();
        Assert.True((await setup.Service.RecordBattleResultAsync(
            setup.BattleId, setup.CurrentRoundId, setup.PlayerAId,
            setup.PlayerAId, ResultType.SpinFinish)).Succeeded);
        Assert.True((await setup.Service.ReviseRoundAsync(
            setup.BattleId, setup.CurrentRoundId, setup.PlayerAId,
            setup.PlayerBId, ResultType.SpinFinish, "取消前修正測試")).Succeeded);

        Assert.False((await setup.Service.CancelQuickBattleAsync(
            setup.BattleId, setup.PlayerAId, false)).Succeeded);
        Assert.False((await setup.Service.CancelQuickBattleAsync(
            setup.BattleId, setup.PlayerBId, true)).Succeeded);
        Assert.True(await setup.Db.Battles.AnyAsync(x => x.Id == setup.BattleId));

        Assert.True((await setup.Service.CancelQuickBattleAsync(
            setup.BattleId, setup.PlayerAId, true)).Succeeded);

        setup.Db.ChangeTracker.Clear();
        Assert.False(await setup.Db.Battles.AnyAsync(x => x.Id == setup.BattleId));
        Assert.False(await setup.Db.BattleLineups.AnyAsync(x => x.BattleId == setup.BattleId));
        Assert.False(await setup.Db.BattleRounds.AnyAsync(x => x.BattleId == setup.BattleId));
        Assert.Empty(await setup.Db.BattleRoundEvents.ToListAsync());
        Assert.Empty(await setup.Db.BattleRoundRevisions.ToListAsync());
        var statistics = new StatisticsService(setup.Db);
        Assert.Equal(0, (await statistics.GetUserSummaryAsync(setup.PlayerAId)).Wins);
        Assert.All(await statistics.GetBeybladeStatisticsAsync(setup.PlayerAId, null),
            x => Assert.Equal(0, x.Wins + x.Losses + x.Score + x.AgainstScore));
    }

    [Fact]
    public async Task AssignSides_BeforeStart_UpdatesSideWithoutChangingParticipants()
    {
        await using var setup = await TestBattle.CreateAsync(startBattle: false);

        var result = await setup.Service.AssignSidesAsync(setup.BattleId, setup.PlayerAId, BattleSide.X);

        Assert.True(result.Succeeded);
        var battle = await setup.Db.Battles.SingleAsync(x => x.Id == setup.BattleId);
        Assert.Equal(BattleSide.X, battle.SideADesignation);
        Assert.Equal(setup.PlayerAId, battle.PlayerAId);
        Assert.Equal(setup.PlayerBId, battle.PlayerBId);
    }

    private sealed class TestBattle : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public AppDbContext Db { get; }
        public BattleService Service { get; }
        public QuickBattleFlowService Flow { get; }
        public int BattleId { get; private init; }
        public int CurrentRoundId { get; private init; }
        public int PlayerAId { get; private init; }
        public int PlayerBId { get; private init; }
        public List<int> PlayerABladeIds { get; private init; } = [];
        public List<int> PlayerBBladeIds { get; private init; } = [];

        private TestBattle(SqliteConnection connection, AppDbContext db)
        {
            _connection = connection;
            Db = db;
            Service = new BattleService(db);
            Flow = new QuickBattleFlowService(db);
        }

        public static async Task<TestBattle> CreateAsync(bool startBattle = true)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var a = new User { Account = "a", PasswordHash = "x", DisplayName = "A", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
            var b = new User { Account = "b", PasswordHash = "x", DisplayName = "B", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
            db.Users.AddRange(a, b);
            await db.SaveChangesAsync();
            var blades = Enumerable.Range(1, 3).Select(i => new Beyblade { UserId = a.Id, Name = $"A{i}", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow })
                .Concat(Enumerable.Range(1, 3).Select(i => new Beyblade { UserId = b.Id, Name = $"B{i}", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow })).ToList();
            db.Beyblades.AddRange(blades);
            await db.SaveChangesAsync();
            var service = new BattleService(db);
            var flow = new QuickBattleFlowService(db);
            var invitation = (await flow.SendInvitationAsync(a.Id, b.Id)).Value!;
            var battleId = (await flow.AcceptInvitationAsync(invitation.Id, b.Id)).Value;
            Assert.True((await flow.SubmitLineupAsync(
                battleId, a.Id, blades.Take(3).Select(x => x.Id).ToList())).Succeeded);
            Assert.True((await flow.SubmitLineupAsync(
                battleId, b.Id, blades.Skip(3).Select(x => x.Id).ToList())).Succeeded);
            Assert.True((await flow.ConfirmLineupAsync(battleId, a.Id)).Succeeded);
            Assert.True((await flow.ConfirmLineupAsync(battleId, b.Id)).Succeeded);
            var roundId = 0;
            if (startBattle)
            {
                await service.AssignSidesAsync(battleId, a.Id, BattleSide.B);
                roundId = (await service.StartBattleAsync(battleId, a.Id)).Value!.Id;
            }
            return new TestBattle(connection, db)
            {
                BattleId = battleId, CurrentRoundId = roundId, PlayerAId = a.Id, PlayerBId = b.Id,
                PlayerABladeIds = blades.Take(3).Select(x => x.Id).ToList(), PlayerBBladeIds = blades.Skip(3).Select(x => x.Id).ToList()
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
