using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.DataMigration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Tests;

public class LegacyLineupReaderTests
{
    [Fact]
    public async Task LegacySqliteLineups_ReadWithoutConfigurationColumns_AndRemainUnknown()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE BattleLineups (
                Id INTEGER PRIMARY KEY, BattleId INTEGER, SequenceNo INTEGER, PositionNo INTEGER,
                PlayerAId INTEGER, PlayerADisplayNameSnapshot TEXT, PlayerABeybladeId INTEGER,
                PlayerABeybladeNameSnapshot TEXT, PlayerBId INTEGER, PlayerBDisplayNameSnapshot TEXT,
                PlayerBBeybladeId INTEGER, PlayerBBeybladeNameSnapshot TEXT, IsCurrent INTEGER
            );
            CREATE TABLE BattleLineupSelections (
                Id INTEGER PRIMARY KEY, BattleId INTEGER, SequenceNo INTEGER, UserId INTEGER,
                PositionNo INTEGER, BeybladeId INTEGER, PlayerDisplayNameSnapshot TEXT,
                BeybladeNameSnapshot TEXT, SubmittedAtUtc TEXT
            );
            INSERT INTO BattleLineups VALUES (7, 3, 2, 1, 11, 'A', 21, 'Old A', 12, 'B', 22, 'Old B', 1);
            INSERT INTO BattleLineupSelections VALUES (8, 3, 2, 11, 1, 21, 'A', 'Old A', '2026-09-01 00:00:00');
            """);
        var lineup = Assert.Single(await LegacyLineupReader.ReadLineupsAsync(db));
        var selection = Assert.Single(await LegacyLineupReader.ReadSelectionsAsync(db));
        Assert.Equal(7, lineup.Id);
        Assert.Equal(21, lineup.PlayerABeybladeId);
        Assert.Equal("Old B", lineup.PlayerBBeybladeNameSnapshot);
        Assert.True(lineup.IsCurrent);
        Assert.Equal(2, selection.SequenceNo);
        Assert.Equal("Old A", selection.BeybladeNameSnapshot);
        Assert.Null(lineup.PlayerAConfigurationId);
        Assert.Null(lineup.PlayerBConfigurationId);
        Assert.Null(selection.BeybladeConfigurationId);
    }

    [Fact]
    public async Task LegacyBeyblades_ReadWithoutUpperNameColumn()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE Beyblades (Id INTEGER PRIMARY KEY, UserId INTEGER, Name TEXT, IsDeleted INTEGER, CreatedAtUtc TEXT, UpdatedAtUtc TEXT);
            INSERT INTO Beyblades VALUES (1, 2, 'legacy', 0, '2026-09-01 00:00:00', '2026-09-01 00:00:00');
            """);
        var blade = Assert.Single(await LegacyLineupReader.ReadBeybladesAsync(db));
        Assert.Equal("legacy", blade.Name);
        Assert.Null(blade.UpperName);
        Assert.Empty(blade.Configurations);
    }
}
