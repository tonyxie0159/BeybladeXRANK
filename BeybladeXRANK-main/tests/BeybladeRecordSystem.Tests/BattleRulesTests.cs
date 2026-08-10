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

        var reordered = await setup.Service.CreateReorderedLineupAsync(setup.BattleId, setup.PlayerAId, setup.PlayerABladeIds.AsEnumerable().Reverse().ToList(), setup.PlayerBBladeIds.AsEnumerable().Reverse().ToList());

        Assert.True(reordered.Succeeded);
        var battle = await setup.Db.Battles.SingleAsync(x => x.Id == setup.BattleId);
        var currentLineup = await setup.Db.BattleLineups.Where(x => x.BattleId == setup.BattleId && x.IsCurrent).OrderBy(x => x.PositionNo).ToListAsync();
        Assert.Equal(3, battle.PlayerAScore);
        Assert.Equal(2, currentLineup[0].SequenceNo);
        Assert.Equal(setup.PlayerABladeIds[2], currentLineup[0].PlayerABeybladeId);
        Assert.Equal(4, reordered.Value!.RoundNo);
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
    }

    private sealed class TestBattle : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public AppDbContext Db { get; }
        public BattleService Service { get; }
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
        }

        public static async Task<TestBattle> CreateAsync()
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
            var battle = (await service.CreateDraftAsync(a.Id, b.Id)).Value!;
            await service.SetLineupAsync(battle.Id, a.Id, blades.Take(3).Select(x => x.Id).ToList(), blades.Skip(3).Select(x => x.Id).ToList());
            await service.LockLineupAsync(battle.Id, a.Id);
            var round = (await service.StartBattleAsync(battle.Id, a.Id)).Value!;
            return new TestBattle(connection, db)
            {
                BattleId = battle.Id, CurrentRoundId = round.Id, PlayerAId = a.Id, PlayerBId = b.Id,
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
