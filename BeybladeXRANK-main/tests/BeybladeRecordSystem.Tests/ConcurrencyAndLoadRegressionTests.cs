using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Domain.Tournaments;
using BeybladeRecordSystem.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Tests;

public class ConcurrencyAndLoadRegressionTests
{
    [Fact]
    public async Task FinalRegistrationSlot_TwoIndependentDbContexts_OnlyOneSucceeds()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"beybladexrank-registration-race-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath};Pooling=False;Default Timeout=5";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connectionString)
            .Options;

        try
        {
            int tournamentId;
            int firstCandidateId;
            int secondCandidateId;
            await using (var seedDb = new AppDbContext(options))
            {
                await seedDb.Database.EnsureCreatedAsync();
                var now = DateTime.UtcNow;
                var organizer = NewUser("race-organizer", now);
                var registered = NewUser("race-registered", now);
                var firstCandidate = NewUser("race-first", now);
                var secondCandidate = NewUser("race-second", now);
                seedDb.Users.AddRange(organizer, registered, firstCandidate, secondCandidate);
                await seedDb.SaveChangesAsync();

                var tournament = new Tournament
                {
                    Name = "Final Slot Race",
                    Mode = TournamentMode.Individual,
                    Format = TournamentFormat.SingleElimination,
                    RegistrationMode = TournamentRegistrationMode.Individual,
                    RuleSet = TournamentRuleSet.IndividualThreeBladeFourPoints,
                    Status = TournamentStatus.RegistrationOpen,
                    RegistrationStage = TournamentRegistrationStage.Open,
                    BeybladesPerPlayer = 3,
                    ScoreToWin = 4,
                    TargetEntryCount = 2,
                    OrganizerUserId = organizer.Id,
                    RulesSnapshot = "race-test",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    Version = Guid.NewGuid().ToByteArray()
                };
                seedDb.Tournaments.Add(tournament);
                await seedDb.SaveChangesAsync();
                seedDb.TournamentEntries.Add(new TournamentEntry
                {
                    TournamentId = tournament.Id,
                    IndividualUserId = registered.Id,
                    RegistrationNumber = "RACE-001",
                    DisplayNameSnapshot = registered.DisplayName,
                    Status = TournamentEntryStatus.Registered,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    RegisteredAtUtc = now
                });
                await seedDb.SaveChangesAsync();

                tournamentId = tournament.Id;
                firstCandidateId = firstCandidate.Id;
                secondCandidateId = secondCandidate.Id;
            }

            await using var firstDb = new AppDbContext(options);
            await using var secondDb = new AppDbContext(options);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstTask = RegisterAfterSignalAsync(firstDb, tournamentId, firstCandidateId, start.Task);
            var secondTask = RegisterAfterSignalAsync(secondDb, tournamentId, secondCandidateId, start.Task);
            start.SetResult();

            var results = await Task.WhenAll(firstTask, secondTask);

            Assert.Single(results, result => result.Succeeded);
            await using var verificationDb = new AppDbContext(options);
            var savedTournament = await verificationDb.Tournaments
                .Include(x => x.Entries)
                .SingleAsync(x => x.Id == tournamentId);
            Assert.Equal(2, savedTournament.Entries.Count(x => x.Status == TournamentEntryStatus.Registered));
            Assert.Equal(TournamentRegistrationStage.CapacityReached, savedTournament.RegistrationStage);
            Assert.Equal(2, savedTournament.Entries.Select(x => x.RegistrationNumber).Distinct().Count());
        }
        finally
        {
            DeleteSqliteFiles(databasePath);
        }
    }

    [Fact]
    public void MaximumSupportedTournamentSizes_GenerateCompleteSchedules()
    {
        var single = TournamentScheduleGenerator.GenerateSingleElimination(
            Enumerable.Range(1, 512), randomSeed: 71);
        var doubleElimination = TournamentScheduleGenerator.GenerateDoubleElimination(
            Enumerable.Range(1, 256), randomSeed: 73);
        var roundRobin = TournamentScheduleGenerator.GenerateRoundRobin(
            Enumerable.Range(1, 32));
        var swiss = SwissPairingGenerator.GenerateRound(
            Enumerable.Range(1, 512)
                .Select(entryId => new SwissEntryStanding(entryId, 0, new HashSet<int>(), false)),
            roundNumber: 1,
            randomSeed: 79);

        Assert.Equal(511, single.Matches.Count);
        Assert.Equal(511, doubleElimination.Matches.Count);
        Assert.Equal(496, roundRobin.Matches.Count);
        Assert.Equal(256, swiss.Pairings.Count);
        Assert.Null(swiss.ByeEntryId);
        Assert.Equal(512, swiss.Pairings
            .SelectMany(x => new[] { x.EntryAId, x.EntryBId })
            .Distinct()
            .Count());
    }

    [Fact]
    public async Task HighFrequencyBattleWrites_PersistEveryFaultAndPenaltyExactlyOnce()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options);
        await db.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        var playerA = NewUser("load-a", now);
        var playerB = NewUser("load-b", now);
        db.Users.AddRange(playerA, playerB);
        await db.SaveChangesAsync();
        var bladeA = new Beyblade { UserId = playerA.Id, Name = "Load A", CreatedAtUtc = now, UpdatedAtUtc = now };
        var bladeB = new Beyblade { UserId = playerB.Id, Name = "Load B", CreatedAtUtc = now, UpdatedAtUtc = now };
        db.Beyblades.AddRange(bladeA, bladeB);
        await db.SaveChangesAsync();

        var rounds = new List<BattleRound>();
        for (var index = 1; index <= 16; index++)
        {
            var battle = new Battle
            {
                SourceType = BattleSourceType.Quick,
                ScoreToWin = 4,
                PlayerAId = playerA.Id,
                PlayerBId = playerB.Id,
                CreatedByUserId = playerA.Id,
                Status = BattleStatus.InProgress,
                SideADesignation = BattleSide.B,
                CreatedAtUtc = now,
                StartedAtUtc = now,
                Version = Guid.NewGuid().ToByteArray()
            };
            var lineup = new BattleLineup
            {
                Battle = battle,
                SequenceNo = 1,
                PositionNo = 1,
                PlayerAId = playerA.Id,
                PlayerADisplayNameSnapshot = playerA.DisplayName,
                PlayerABeybladeId = bladeA.Id,
                PlayerABeybladeNameSnapshot = bladeA.Name,
                PlayerBId = playerB.Id,
                PlayerBDisplayNameSnapshot = playerB.DisplayName,
                PlayerBBeybladeId = bladeB.Id,
                PlayerBBeybladeNameSnapshot = bladeB.Name,
                IsCurrent = true
            };
            var round = new BattleRound
            {
                Battle = battle,
                Lineup = lineup,
                RoundNo = 1,
                PositionNo = 1,
                PlayerAId = playerA.Id,
                PlayerADisplayNameSnapshot = playerA.DisplayName,
                PlayerABeybladeId = bladeA.Id,
                PlayerABeybladeNameSnapshot = bladeA.Name,
                PlayerBId = playerB.Id,
                PlayerBDisplayNameSnapshot = playerB.DisplayName,
                PlayerBBeybladeId = bladeB.Id,
                PlayerBBeybladeNameSnapshot = bladeB.Name,
                Status = BattleRoundStatus.InProgress,
                CreatedAtUtc = now
            };
            db.BattleRounds.Add(round);
            rounds.Add(round);
        }
        await db.SaveChangesAsync();

        var service = new BattleService(db);
        foreach (var round in rounds)
        {
            for (var write = 0; write < 6; write++)
            {
                var result = await service.RecordLaunchFaultAsync(
                    round.BattleId, round.Id, playerA.Id, playerA.Id);
                Assert.True(result.Succeeded, result.Error);
            }
        }

        db.ChangeTracker.Clear();
        var battles = await db.Battles.OrderBy(x => x.Id).ToListAsync();
        var events = await db.BattleRoundEvents.ToListAsync();
        Assert.Equal(96, events.Count(x => x.EventType == BattleRoundEventType.LaunchFault));
        Assert.Equal(48, events.Count(x => x.EventType == BattleRoundEventType.LaunchFaultPenalty));
        Assert.Equal(Enumerable.Range(1, 9), events
            .Where(x => x.BattleRoundId == rounds[0].Id)
            .OrderBy(x => x.EventSequence)
            .Select(x => x.EventSequence));
        Assert.All(battles, battle =>
        {
            Assert.Equal(0, battle.SideAScore);
            Assert.Equal(3, battle.SideBScore);
            Assert.Equal(BattleStatus.InProgress, battle.Status);
        });
    }

    private static async Task<ServiceResult> RegisterAfterSignalAsync(
        AppDbContext db,
        int tournamentId,
        int userId,
        Task start)
    {
        await start;
        return await new TournamentService(db).RegisterIndividualAsync(tournamentId, userId);
    }

    private static User NewUser(string account, DateTime now) => new()
    {
        Account = account,
        PasswordHash = "test-only",
        DisplayName = account,
        CreatedAtUtc = now,
        UpdatedAtUtc = now
    };

    private static void DeleteSqliteFiles(string databasePath)
    {
        foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
