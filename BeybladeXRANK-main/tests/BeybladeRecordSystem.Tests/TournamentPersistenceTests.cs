using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Domain.Tournaments;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BeybladeRecordSystem.Tests;

public class TournamentPersistenceTests
{
    [Fact]
    public async Task Migrations_CreateTournamentTablesAlongsideExistingBattleTables()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);

        await context.Database.MigrateAsync();

        var tables = new HashSet<string>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Contains("Battles", tables);
        Assert.Contains("Tournaments", tables);
        Assert.Contains("TournamentEntries", tables);
        Assert.Contains("TournamentEntryMembers", tables);
        Assert.Contains("TournamentInvitations", tables);
        Assert.Contains("TournamentMatches", tables);
        Assert.Contains("TournamentMatchParticipants", tables);
        Assert.Contains("BattleLineupSelections", tables);
        Assert.Contains("BattleTeamOrderSelections", tables);
    }

    [Fact]
    public async Task BattleGeneralizationMigration_PreservesScoresAndBackfillsPlayerSnapshots()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20260808152245_InitialCreate");

        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO Users (Id, Account, PasswordHash, DisplayName, CreatedAtUtc, UpdatedAtUtc)
            VALUES (1, 'a', 'hash', 'Player A', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
                   (2, 'b', 'hash', 'Player B', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
            INSERT INTO Beyblades (Id, UserId, Name, IsDeleted, CreatedAtUtc, UpdatedAtUtc)
            VALUES (1, 1, 'Blade A', 0, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
                   (2, 2, 'Blade B', 0, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
            INSERT INTO Battles (Id, PlayerAId, PlayerBId, CreatedByUserId, Status, PlayerAScore, PlayerBScore, WinningPlayerId, CreatedAtUtc, Version)
            VALUES (1, 1, 2, 1, 2, 3, 2, NULL, CURRENT_TIMESTAMP, X'01');
            INSERT INTO BattleLineups (Id, BattleId, SequenceNo, PositionNo, PlayerABeybladeId, PlayerABeybladeNameSnapshot, PlayerBBeybladeId, PlayerBBeybladeNameSnapshot, IsCurrent)
            VALUES (1, 1, 1, 1, 1, 'Blade A', 2, 'Blade B', 1);
            INSERT INTO BattleRounds (Id, BattleId, LineupId, RoundNo, PositionNo, PlayerABeybladeId, PlayerABeybladeNameSnapshot, PlayerBBeybladeId, PlayerBBeybladeNameSnapshot, Status, CreatedAtUtc)
            VALUES (1, 1, 1, 1, 1, 1, 'Blade A', 2, 'Blade B', 0, CURRENT_TIMESTAMP);
            """);

        await migrator.MigrateAsync();
        context.ChangeTracker.Clear();

        var battle = await context.Battles.SingleAsync();
        var lineup = await context.BattleLineups.SingleAsync();
        var round = await context.BattleRounds.SingleAsync();
        Assert.Equal(3, battle.SideAScore);
        Assert.Equal(2, battle.SideBScore);
        Assert.Equal(4, battle.ScoreToWin);
        Assert.Equal(BattleSourceType.Quick, battle.SourceType);
        Assert.Equal(1, lineup.PlayerAId);
        Assert.Equal("Player A", lineup.PlayerADisplayNameSnapshot);
        Assert.Equal(2, lineup.PlayerBId);
        Assert.Equal("Player B", lineup.PlayerBDisplayNameSnapshot);
        Assert.Equal(1, round.PlayerAId);
        Assert.Equal("Player A", round.PlayerADisplayNameSnapshot);
        Assert.Equal(2, round.PlayerBId);
        Assert.Equal("Player B", round.PlayerBDisplayNameSnapshot);
    }

    [Fact]
    public async Task LineupSequenceMigration_BackfillsExistingPrivateSubmissionsAsSequenceOne()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20260819165152_AddTeamMatchLineupFlow");
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO Users (Id, Account, PasswordHash, DisplayName, CreatedAtUtc, UpdatedAtUtc)
            VALUES (1, 'organizer-seq', 'hash', 'Organizer', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
                   (2, 'player-seq', 'hash', 'Player', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
            INSERT INTO Beyblades (Id, UserId, Name, IsDeleted, CreatedAtUtc, UpdatedAtUtc)
            VALUES (1, 2, 'Existing Blade', 0, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
            INSERT INTO Tournaments (Id, Name, Mode, Format, RegistrationMode, RuleSet, Status, RegistrationStage, TeamSize, BeybladesPerPlayer, ScoreToWin, TargetEntryCount, OrganizerUserId, RulesSnapshot, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES (1, 'Existing Cup', 1, 0, 1, 1, 1, 5, 2, 3, 8, 2, 1, 'snapshot', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, X'01');
            INSERT INTO TournamentEntries (Id, TournamentId, DisplayNameSnapshot, Status, CreatedAtUtc, UpdatedAtUtc)
            VALUES (1, 1, 'Existing Team', 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
            INSERT INTO Battles (Id, SourceType, ScoreToWin, CreatedByUserId, Status, SideAScore, SideBScore, CreatedAtUtc, Version)
            VALUES (1, 0, 4, 1, 0, 0, 0, CURRENT_TIMESTAMP, X'01');
            INSERT INTO BattleLineupSelections (Id, BattleId, UserId, PositionNo, BeybladeId, PlayerDisplayNameSnapshot, BeybladeNameSnapshot, SubmittedAtUtc)
            VALUES (1, 1, 2, 1, 1, 'Player', 'Existing Blade', CURRENT_TIMESTAMP);
            INSERT INTO BattleTeamOrderSelections (Id, BattleId, TournamentEntryId, UserId, PositionNo, SubmittedByUserId, SubmittedAtUtc)
            VALUES (1, 1, 1, 2, 1, 2, CURRENT_TIMESTAMP);
            """);

        await migrator.MigrateAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT SequenceNo FROM BattleLineupSelections), (SELECT SequenceNo FROM BattleTeamOrderSelections)";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(1, reader.GetInt32(1));
    }

    [Fact]
    public async Task DuplicateRegistrationNumberWithinTournament_IsRejected()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.MigrateAsync();

        var organizer = CreateUser("organizer");
        var tournament = CreateIndividualTournament(organizer);
        context.AddRange(organizer, tournament);
        await context.SaveChangesAsync();

        context.TournamentEntries.AddRange(
            CreateEntry(tournament.Id, "A-001", "Player 1"),
            CreateEntry(tournament.Id, "A-001", "Player 2"));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task SamePlayerCannotBelongToTwoEntriesInOneTournament()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.MigrateAsync();

        var organizer = CreateUser("organizer");
        var player = CreateUser("player");
        var tournament = CreateTeamTournament(organizer);
        var firstEntry = CreateEntry(0, "T-001", "Team 1");
        var secondEntry = CreateEntry(0, "T-002", "Team 2");
        tournament.Entries.Add(firstEntry);
        tournament.Entries.Add(secondEntry);
        context.AddRange(organizer, player, tournament);
        await context.SaveChangesAsync();

        context.TournamentEntryMembers.AddRange(
            CreateMember(tournament.Id, firstEntry.Id, player.Id, 1),
            CreateMember(tournament.Id, secondEntry.Id, player.Id, 1));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public void TournamentAndMatchVersions_AreConcurrencyTokens()
    {
        using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:").Options);

        Assert.True(context.Model.FindEntityType(typeof(Tournament))!
            .FindProperty(nameof(Tournament.Version))!.IsConcurrencyToken);
        Assert.True(context.Model.FindEntityType(typeof(TournamentMatch))!
            .FindProperty(nameof(TournamentMatch.Version))!.IsConcurrencyToken);
        Assert.True(context.Model.FindEntityType(typeof(TournamentMatchParticipant))!
            .FindProperty(nameof(TournamentMatchParticipant.Version))!.IsConcurrencyToken);
    }

    [Fact]
    public async Task TournamentTeamBattle_CanLinkMatchWithoutLegacyTopLevelPlayers()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.MigrateAsync();

        var organizer = CreateUser("organizer");
        var tournament = CreateTeamTournament(organizer);
        var match = new TournamentMatch
        {
            Tournament = tournament,
            Bracket = TournamentBracket.Winners,
            RoundNumber = 1,
            MatchNumber = 1,
            SequenceNumber = 1,
            SideASourceKind = TournamentParticipantSourceKind.Entry,
            SideASourceReferenceId = 1,
            SideBSourceKind = TournamentParticipantSourceKind.Entry,
            SideBSourceReferenceId = 2,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            Version = [1]
        };
        context.AddRange(organizer, tournament, match);
        await context.SaveChangesAsync();

        context.Battles.Add(new Battle
        {
            SourceType = BattleSourceType.TournamentTeam,
            ScoreToWin = 8,
            TournamentMatchId = match.Id,
            CreatedByUserId = organizer.Id,
            SideADesignation = BattleSide.B,
            CreatedAtUtc = DateTime.UtcNow,
            Version = [1]
        });
        await context.SaveChangesAsync();

        var battle = await context.Battles.Include(x => x.TournamentMatch).SingleAsync();
        Assert.Null(battle.PlayerAId);
        Assert.Null(battle.PlayerBId);
        Assert.Equal(match.Id, battle.TournamentMatchId);
        Assert.Equal(8, battle.ScoreToWin);
    }

    private static AppDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);

    private static User CreateUser(string account) => new()
    {
        Account = account,
        PasswordHash = "hash",
        DisplayName = account,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static Tournament CreateIndividualTournament(User organizer) => new()
    {
        Name = "Individual Cup",
        Mode = TournamentMode.Individual,
        Format = TournamentFormat.SingleElimination,
        RegistrationMode = TournamentRegistrationMode.Individual,
        RuleSet = TournamentRuleSet.IndividualThreeBladeFourPoints,
        TeamSize = null,
        BeybladesPerPlayer = 3,
        ScoreToWin = 4,
        TargetEntryCount = 8,
        OrganizerUser = organizer,
        RulesSnapshot = "snapshot",
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        Version = [1]
    };

    private static Tournament CreateTeamTournament(User organizer) => new()
    {
        Name = "Team Cup",
        Mode = TournamentMode.Team,
        Format = TournamentFormat.DoubleElimination,
        RegistrationMode = TournamentRegistrationMode.CompleteTeam,
        RuleSet = TournamentRuleSet.DuoSixBladeEightPoints,
        TeamSize = 2,
        BeybladesPerPlayer = 3,
        ScoreToWin = 8,
        TargetEntryCount = 8,
        OrganizerUser = organizer,
        RulesSnapshot = "snapshot",
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        Version = [1]
    };

    private static TournamentEntry CreateEntry(int tournamentId, string number, string name) => new()
    {
        TournamentId = tournamentId,
        RegistrationNumber = number,
        DisplayNameSnapshot = name,
        Status = TournamentEntryStatus.Registered,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static TournamentEntryMember CreateMember(int tournamentId, int entryId, int userId, int order) => new()
    {
        TournamentId = tournamentId,
        TournamentEntryId = entryId,
        UserId = userId,
        MemberOrder = order,
        DisplayNameSnapshot = $"Player {userId}",
        JoinedAtUtc = DateTime.UtcNow
    };
}
