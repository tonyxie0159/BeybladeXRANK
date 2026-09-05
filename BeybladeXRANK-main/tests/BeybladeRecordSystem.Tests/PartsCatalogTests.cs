using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BeybladeRecordSystem.Tests;

public class PartsCatalogTests
{
    [Fact]
    public void ApprovedCatalog_Has279Names_WithCorrectDeduplicationAndIntegration()
    {
        var entries = PartCatalog.Read();
        Assert.Equal(279, entries.Count);
        Assert.Equal(279, entries.Select(x => (x.Category, x.Name)).Distinct().Count());
        var counts = new[] { 112, 37, 53, 28, 16, 7, 7, 19 };
        foreach (var category in Enum.GetValues<PartCategory>())
            Assert.Equal(counts[(int)category], entries.Count(x => x.Category == category));
        Assert.Single(entries, x => x.Name == "時鐘幻象");
        Assert.Single(entries, x => x.Name == "雷霆天龍");
        Assert.Contains(entries, x => x.Name == "地獄鐮刀");
        Assert.Contains(entries, x => x.Name == "惡魔紅鐮");
        Assert.Contains(entries, x => x.Name == "Clamp Crab");
        Assert.Equal(3, entries.Count(x => x.Name == "F"));
        Assert.Equal(5, entries.Count(x => x.IntegratesRatchet));
        Assert.Contains(entries, x => x.Name == "Tr" && x.IntegratesRatchet && x.Category == PartCategory.Bit);
        Assert.Contains(entries, x => x.Name == "Nr" && !x.IntegratesRatchet);
        Assert.DoesNotContain(entries, x => x.Name.Contains("金屬塗層") || x.Name.Contains("一體式") ||
            x.Name.Contains("套組") || x.Name.Contains("／") || x.Name.Contains("■") ||
            x.Name.Contains("雷霆天龍 (") || x.Name == "蒼龍勇氣S");
    }

    [Fact]
    public async Task Import_IsIdempotent_PreservesIdsTimestampsAndDisabledParts()
    {
        await using var fixture = await Fixture.CreateAsync();
        var ids = await fixture.Db.Parts.ToDictionaryAsync(x => x.Id, x => x.CreatedAtUtc);
        var seriesCount = await fixture.Db.PartSeries.CountAsync();
        fixture.Db.Parts.First().IsActive = false;
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(0, await PartCatalog.ImportAsync(fixture.Db));
        Assert.Equal(279, await fixture.Db.Parts.CountAsync());
        Assert.Equal(seriesCount, await fixture.Db.PartSeries.CountAsync());
        Assert.Single(await fixture.Db.Parts.Where(x => !x.IsActive).ToListAsync());
        Assert.All(await fixture.Db.Parts.ToListAsync(), x => Assert.Equal(ids[x.Id], x.CreatedAtUtc));
        Assert.True(await fixture.Db.PartSeries.Where(x => x.Part.Category == PartCategory.Ratchet && x.Part.Name == "4-60").CountAsync() > 1);
    }

    [Theory]
    [InlineData("Blade:時鐘幻象,Ratchet:4-55,Bit:S")]
    [InlineData("Blade:榮耀武神,Bit:LF")]
    [InlineData("Blade:惡魔冥界,Bit:Z")]
    [InlineData("Blade:彈丸獅鷲,Bit:H")]
    [InlineData("Blade:時鐘幻象,Bit:Tr")]
    [InlineData("LockChip:蒼龍,MainBlade:勇氣,AssistBlade:S,Ratchet:6-60,Bit:V")]
    [InlineData("LockChip:天馬,MainBlade:爆擊,AssistBlade:A,Bit:Tr")]
    [InlineData("LockChip:帝王,MainBlade:威能,AssistBlade:H,Bit:Op")]
    [InlineData("LockChip:龍王,OverBlade:B,MetalBlade:閃擊,AssistBlade:K,Ratchet:1-50,Bit:I")]
    [InlineData("LockChip:龍王,OverBlade:F,MetalBlade:閃擊,AssistBlade:F,Ratchet:1-50,Bit:F")]
    [InlineData("LockChip:龍王,OverBlade:B,MetalBlade:閃擊,AssistBlade:K,Bit:Op")]
    public void Assembly_AcceptsCompleteStructures(string selection) =>
        Assert.Null(BeybladeAssemblyRules.Validate(Select(selection)));

    [Theory]
    [InlineData("")]
    [InlineData("Blade:時鐘幻象,Bit:S")]
    [InlineData("Blade:時鐘幻象,Ratchet:4-55")]
    [InlineData("Blade:時鐘幻象,Blade:蒼龍神劍,Ratchet:4-55,Bit:S")]
    [InlineData("Blade:時鐘幻象,Ratchet:4-55,Bit:S,Bit:F")]
    [InlineData("Blade:榮耀武神,Ratchet:4-55,Bit:LF")]
    [InlineData("Blade:榮耀武神,Bit:Tr")]
    [InlineData("Blade:時鐘幻象,Ratchet:4-55,Bit:Tr")]
    [InlineData("LockChip:蒼龍,MainBlade:勇氣,Ratchet:4-55,Bit:S")]
    [InlineData("MainBlade:勇氣,AssistBlade:S,Ratchet:4-55,Bit:S")]
    [InlineData("LockChip:蒼龍,OverBlade:B,AssistBlade:S,Ratchet:4-55,Bit:S")]
    [InlineData("LockChip:蒼龍,MetalBlade:閃擊,AssistBlade:S,Ratchet:4-55,Bit:S")]
    [InlineData("LockChip:蒼龍,MainBlade:勇氣,OverBlade:B,MetalBlade:閃擊,AssistBlade:S,Bit:Tr")]
    [InlineData("Blade:時鐘幻象,LockChip:蒼龍,Ratchet:4-55,Bit:S")]
    public void Assembly_RejectsMissingConflictingAndMixedParts(string selection) =>
        Assert.NotNull(BeybladeAssemblyRules.Validate(Select(selection)));

    [Fact]
    public async Task Record_EnforcesOwnershipCompleteConfigurationAndOneTimeBackfill()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new BeybladeConfigurationService(fixture.Db);
        var ids = await fixture.IdsAsync();
        Assert.False((await service.RecordAsync(fixture.Other.Id, fixture.Blade.Id, ids)).Succeeded);
        Assert.Null(await service.GetMineAsync(fixture.Other.Id, fixture.Blade.Id));
        Assert.False((await service.RecordAsync(fixture.Owner.Id, fixture.Blade.Id, ids.Take(2).ToArray())).Succeeded);
        Assert.False((await service.RecordAsync(fixture.Owner.Id, fixture.Blade.Id, [ids[0], ids[0]])).Succeeded);
        Assert.False((await service.RecordAsync(fixture.Owner.Id, fixture.Blade.Id, [ids[0], int.MaxValue])).Succeeded);
        Assert.Empty(await fixture.Db.BeybladeConfigurations.ToListAsync());
        Assert.Empty(await fixture.Db.BeybladeConfigurationParts.ToListAsync());
        Assert.True((await service.RecordAsync(fixture.Owner.Id, fixture.Blade.Id, ids)).Succeeded);
        Assert.True((await service.RecordAsync(fixture.Owner.Id, fixture.Blade.Id, ids)).Succeeded);
        Assert.Single(await fixture.Db.BeybladeConfigurations.ToListAsync());
        Assert.Equal(3, await fixture.Db.BeybladeConfigurationParts.CountAsync());
        Assert.Equal(3, (await service.GetMineAsync(fixture.Owner.Id, fixture.Blade.Id))!.Parts.Count);
        var part = await fixture.Db.Parts.SingleAsync(x => x.Id == ids[0]);
        var originalName = part.Name;
        part.Name = "目錄顯示修正";
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(originalName, (await service.GetMineAsync(fixture.Owner.Id, fixture.Blade.Id))!
            .Parts.Single(x => x.PartId == part.Id).PartNameSnapshot);
    }

    [Fact]
    public async Task Record_RejectsInactivePartsAndDeletedBeyblades()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new BeybladeConfigurationService(fixture.Db);
        var ids = await fixture.IdsAsync();
        var part = await fixture.Db.Parts.FindAsync(ids[0]);
        part!.IsActive = false;
        await fixture.Db.SaveChangesAsync();
        Assert.DoesNotContain(await service.GetActivePartsAsync(), x => x.Id == part.Id);
        Assert.False((await service.RecordAsync(fixture.Owner.Id, fixture.Blade.Id, ids)).Succeeded);
        part.IsActive = true;
        fixture.Blade.IsDeleted = true;
        await fixture.Db.SaveChangesAsync();
        Assert.False((await service.RecordAsync(fixture.Owner.Id, fixture.Blade.Id, ids)).Succeeded);
    }

    [Fact]
    public async Task SavedConfigurations_CannotBeModifiedOrExtended()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.True((await new BeybladeConfigurationService(fixture.Db)
            .RecordAsync(fixture.Owner.Id, fixture.Blade.Id, await fixture.IdsAsync())).Succeeded);
        var configuration = await fixture.Db.BeybladeConfigurations.Include(x => x.Parts).SingleAsync();
        configuration.CreatedAtUtc = DateTime.UtcNow.AddHours(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Db.SaveChangesAsync());
        fixture.Db.ChangeTracker.Clear();
        configuration = await fixture.Db.BeybladeConfigurations.Include(x => x.Parts).SingleAsync();
        configuration.Parts.Add(new BeybladeConfigurationPart
        {
            PartId = await fixture.Db.Parts.Where(x => x.Category == PartCategory.LockChip).Select(x => x.Id).FirstAsync(),
            PartNameSnapshot = "invalid extension"
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Schema_RejectsAnotherBeybladesConfigurationInHistoricalLineup()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.True((await new BeybladeConfigurationService(fixture.Db)
            .RecordAsync(fixture.Owner.Id, fixture.Blade.Id, await fixture.IdsAsync())).Succeeded);
        var secondBlade = new Beyblade { UserId = fixture.Other.Id, Name = "other" };
        fixture.Db.Beyblades.Add(secondBlade);
        var battle = new Battle { PlayerAId = fixture.Owner.Id, PlayerBId = fixture.Other.Id, CreatedByUserId = fixture.Owner.Id };
        fixture.Db.Battles.Add(battle);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.BattleLineupSelections.Add(new BattleLineupSelection
        {
            BattleId = battle.Id, SequenceNo = 1, PositionNo = 1, UserId = fixture.Other.Id,
            BeybladeId = secondBlade.Id,
            BeybladeConfigurationId = await fixture.Db.BeybladeConfigurations.Select(x => x.Id).SingleAsync()
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Db.SaveChangesAsync());
    }

    [Fact]
    public void Migration_IsAdditive_AndHistoricalConfigurationColumnsAreNullable()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
        var script = db.GetService<IMigrator>().GenerateScript("20260903154059_PostgreSqlInitial");
        Assert.Contains("CREATE TABLE \"Parts\"", script);
        Assert.Contains("CREATE TABLE \"BeybladeConfigurations\"", script);
        Assert.Contains("ADD \"BeybladeConfigurationId\" integer", script);
        Assert.Contains("ON DELETE RESTRICT", script);
        Assert.DoesNotContain("UPDATE \"Battle", script);
        Assert.DoesNotContain("DROP TABLE", script);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData("Blade:鮫鯊狂鱗,Ratchet:1-50,Bit:J", "鮫鯊狂鱗1-50J")]
    [InlineData("Blade:榮耀武神,Bit:LR", "榮耀武神LR")]
    [InlineData("Blade:武士星劍,Bit:Op", "武士星劍Op")]
    [InlineData("LockChip:腕龍,OverBlade:F,MetalBlade:極變,AssistBlade:A,Ratchet:1-50,Bit:J", "腕龍極變1-50J")]
    [InlineData("LockChip:帝王,MainBlade:閃焰,AssistBlade:F,Ratchet:4-55,Bit:S", "帝王閃焰4-55S")]
    [InlineData("LockChip:帝王,MainBlade:閃焰,AssistBlade:F,Bit:Tr", "帝王閃焰Tr")]
    public void CommonName_UsesCanonicalCaseAndOmitsOnlyCxOverAndAssist(string selection, string expected)
    {
        var parts = Select(selection);
        Assert.Null(BeybladeAssemblyRules.Validate(parts));
        var configuration = new BeybladeConfiguration
        {
            Parts = parts.Select(x => new BeybladeConfigurationPart { Part = x, PartNameSnapshot = x.Name }).ToList()
        };
        Assert.Equal(expected, configuration.CommonName);
        parts.ForEach(x => x.Name = "目錄新名稱");
        Assert.Equal(expected, configuration.CommonName);
        Assert.Equal("我的陀螺 · " + expected, new Beyblade { Name = "我的陀螺", Configuration = configuration }.DisplayName);
    }

    [Fact]
    public async Task Create_RequiresCompleteParts_AndPersistsNameAndConfigurationTogether()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new BeybladeService(fixture.Db);
        var ids = await fixture.IdsAsync();
        foreach (var invalid in new[] { Array.Empty<int>(), ids.Take(2).ToArray(), new[] { ids[0], ids[0] }, new[] { ids[0], int.MaxValue } })
            Assert.False((await service.CreateAsync(fixture.Owner.Id, "new", invalid)).Succeeded);
        Assert.False(await fixture.Db.Beyblades.AnyAsync(x => x.Name == "new"));
        Assert.Empty(await fixture.Db.BeybladeConfigurations.ToListAsync());
        var bit = await fixture.Db.Parts.SingleAsync(x => x.Id == ids[2]);
        bit.IsActive = false;
        await fixture.Db.SaveChangesAsync();
        Assert.False((await service.CreateAsync(fixture.Owner.Id, "new", ids)).Succeeded);
        bit.IsActive = true;
        await fixture.Db.SaveChangesAsync();
        Assert.True((await service.CreateAsync(fixture.Owner.Id, "  new  ", ids)).Succeeded);
        var blade = (await service.GetMyBeybladesAsync(fixture.Owner.Id)).Single(x => x.Name == "new");
        Assert.Equal("new · 時鐘幻象4-55S", blade.DisplayName);
        Assert.Single(await fixture.Db.BeybladeConfigurations.ToListAsync());
        Assert.False((await service.CreateAsync(fixture.Owner.Id, "new", ids)).Succeeded);
        Assert.Single(await fixture.Db.BeybladeConfigurations.ToListAsync());
    }

    [Fact]
    public async Task BackfillWithRename_IsAtomic_AndCannotRenameOnInvalidOrRepeatedSubmission()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new BeybladeConfigurationService(fixture.Db);
        var ids = await fixture.IdsAsync();
        Assert.False((await service.RecordAsync(fixture.Owner.Id, fixture.Blade.Id, ids.Take(2).ToArray(), "changed")).Succeeded);
        Assert.Equal("legacy", (await fixture.Db.Beyblades.AsNoTracking().SingleAsync()).Name);
        Assert.False((await service.RecordAsync(fixture.Other.Id, fixture.Blade.Id, ids, "stolen")).Succeeded);
        Assert.False((await service.RecordAsync(fixture.Owner.Id, fixture.Blade.Id, ids, " ")).Succeeded);
        Assert.True((await service.RecordAsync(fixture.Owner.Id, fixture.Blade.Id, ids, "renamed")).Succeeded);
        Assert.True((await service.RecordAsync(fixture.Owner.Id, fixture.Blade.Id, ids, "second")).Succeeded);
        Assert.Single(await fixture.Db.BeybladeConfigurations.ToListAsync());
        Assert.Equal("second", (await fixture.Db.Beyblades.AsNoTracking().SingleAsync()).Name);
        Assert.Equal("時鐘幻象4-55S", (await service.GetMineAsync(fixture.Owner.Id, fixture.Blade.Id))!.CommonName);
    }

    [Fact]
    public async Task CxVersions_KeepSameUpper_IncludeHiddenComponents_AndReuseIdenticalParts()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new BeybladeConfigurationService(fixture.Db);
        var entries = Select("LockChip:腕龍,OverBlade:B,MetalBlade:極變,AssistBlade:A,Ratchet:1-50,Bit:J");
        var all = await fixture.Db.Parts.ToListAsync();
        var firstIds = entries.Select(x => all.Single(p => p.Category == x.Category && p.Name == x.Name).Id).ToArray();
        Assert.True((await service.RecordAsync(fixture.Owner.Id, fixture.Blade.Id, firstIds)).Succeeded);
        var first = (await service.GetVersionsAsync(fixture.Owner.Id, fixture.Blade.Id)).Single();
        var changed = firstIds.Where(id => all.Single(p => p.Id == id).Category != PartCategory.AssistBlade)
            .Append(all.Single(p => p.Category == PartCategory.AssistBlade && p.Name == "F").Id).ToArray();
        Assert.True((await service.RecordAsync(fixture.Owner.Id, fixture.Blade.Id, changed)).Succeeded);
        var third = changed.Where(id => all.Single(p => p.Id == id).Category != PartCategory.OverBlade)
            .Append(all.Single(p => p.Category == PartCategory.OverBlade && p.Name == "F").Id).ToArray();
        Assert.True((await service.RecordAsync(fixture.Owner.Id, fixture.Blade.Id, third)).Succeeded);
        Assert.True((await service.RecordAsync(fixture.Owner.Id, fixture.Blade.Id, firstIds.Reverse().ToArray())).Succeeded);
        var versions = await service.GetVersionsAsync(fixture.Owner.Id, fixture.Blade.Id);
        Assert.Equal(new[] { 3, 2, 1 }, versions.Select(x => x.VersionNo));
        Assert.All(versions, x => Assert.Equal("腕龍極變1-50J", x.CommonName));
        Assert.Equal(first.Id, versions.Single(x => x.VersionNo == 1).Id);
        Assert.Equal(3, versions.Select(x => x.PartsKey).Distinct().Count());
        Assert.Equal("腕龍極變", fixture.Blade.UpperName);
        var otherUpper = firstIds.Where(id => all.Single(p => p.Id == id).Category != PartCategory.LockChip)
            .Append(all.Single(p => p.Category == PartCategory.LockChip && p.Name == "帝王").Id).ToArray();
        Assert.False((await service.RecordAsync(fixture.Owner.Id, fixture.Blade.Id, otherUpper)).Succeeded);
        Assert.False((await service.RecordAsync(fixture.Other.Id, fixture.Blade.Id, third)).Succeeded);
        Assert.False((await new BeybladeService(fixture.Db).CreateAsync(fixture.Owner.Id, "same upper", firstIds)).Succeeded);
        Assert.True((await new BeybladeService(fixture.Db).CreateAsync(fixture.Other.Id, "same upper", firstIds)).Succeeded);
    }

    [Fact]
    public async Task VersionStatistics_SumEffectiveEvents_KeepUnknownHistory_AndEnforceOwner()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new BeybladeConfigurationService(fixture.Db);
        var ids = await fixture.IdsAsync();
        Assert.True((await service.RecordAsync(fixture.Owner.Id, fixture.Blade.Id, ids)).Succeeded);
        var v1 = (await service.GetMineAsync(fixture.Owner.Id, fixture.Blade.Id))!.Id;
        var second = ids.ToArray();
        second[1] = await fixture.Db.Parts.Where(x => x.Category == PartCategory.Ratchet && x.Name == "1-50").Select(x => x.Id).SingleAsync();
        Assert.True((await service.RecordAsync(fixture.Owner.Id, fixture.Blade.Id, second)).Succeeded);
        var v2 = (await service.GetMineAsync(fixture.Owner.Id, fixture.Blade.Id))!.Id;
        var otherBlade = new Beyblade { UserId = fixture.Other.Id, Name = "opponent" };
        fixture.Db.Add(otherBlade);
        await fixture.Db.SaveChangesAsync();
        void AddRound(int? version, bool ownerIsA, bool won, int score, BattleSourceType source)
        {
            var a = ownerIsA ? fixture.Owner.Id : fixture.Other.Id;
            var b = ownerIsA ? fixture.Other.Id : fixture.Owner.Id;
            var battle = new Battle { PlayerAId = a, PlayerBId = b, CreatedByUserId = fixture.Owner.Id,
                Status = BattleStatus.Completed, SourceType = source, SideADesignation = BattleSide.B };
            var lineup = new BattleLineup { Battle = battle, SequenceNo = 1, PositionNo = 1,
                PlayerAId = a, PlayerBId = b, PlayerABeybladeId = ownerIsA ? fixture.Blade.Id : otherBlade.Id,
                PlayerBBeybladeId = ownerIsA ? otherBlade.Id : fixture.Blade.Id,
                PlayerAConfigurationId = ownerIsA ? version : null, PlayerBConfigurationId = ownerIsA ? null : version };
            fixture.Db.Add(new BattleRound { Battle = battle, Lineup = lineup, RoundNo = 1, PositionNo = 1,
                PlayerAId = a, PlayerBId = b, PlayerABeybladeId = lineup.PlayerABeybladeId, PlayerBBeybladeId = lineup.PlayerBBeybladeId,
                PlayerABeybladeNameSnapshot = "same", PlayerBBeybladeNameSnapshot = "same", Status = BattleRoundStatus.Completed,
                Events = new List<BattleRoundEvent> {
                    new() { EventSequence = 1, EventType = BattleRoundEventType.BattleResult, ResultType = ResultType.SpinFinish,
                        WinnerPlayerId = won ? fixture.Owner.Id : fixture.Other.Id, ScoreAwarded = score },
                    new() { EventSequence = 2, EventType = BattleRoundEventType.BattleResult, ResultType = ResultType.Extreme,
                        WinnerPlayerId = fixture.Owner.Id, ScoreAwarded = 99, IsEffective = false }
                } });
        }
        AddRound(v1, true, true, 1, BattleSourceType.Quick);
        AddRound(v2, false, false, 2, BattleSourceType.Quick);
        AddRound(null, true, true, 3, BattleSourceType.Quick);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var stats = new StatisticsService(fixture.Db);
        var detail = (await stats.GetBeybladeVersionStatisticsAsync(fixture.Owner.Id, fixture.Blade.Id))!;
        Assert.Equal((2, 1, 4, 2, 3), (detail.Total.Wins, detail.Total.Losses, detail.Total.Score, detail.Total.AgainstScore, detail.Total.RoundCount));
        Assert.Equal(3, detail.Versions.Count);
        Assert.Equal(detail.Total.Score, detail.Versions.Sum(x => x.Summary.Score));
        Assert.Equal(detail.Total.Wins, detail.Versions.Sum(x => x.Summary.Wins));
        Assert.Equal(1, detail.Versions.Single(x => x.ConfigurationId == v1).Summary.Wins);
        Assert.Equal(1, detail.Versions.Single(x => x.ConfigurationId == v2).Summary.Losses);
        Assert.Equal(3, detail.Versions.Single(x => x.ConfigurationId == null).Summary.Score);
        Assert.Equal(1, (await stats.GetBeybladeVersionStatisticsAsync(fixture.Owner.Id, fixture.Blade.Id,
            StatisticsSourceFilter.Quick, StatisticsSideFilter.X))!.Total.Losses);
        Assert.Equal(0, (await stats.GetBeybladeVersionStatisticsAsync(fixture.Owner.Id, fixture.Blade.Id,
            StatisticsSourceFilter.TournamentIndividual))!.Total.RoundCount);
        Assert.Equal(2, (await stats.GetBeybladeVersionStatisticsAsync(fixture.Owner.Id, fixture.Blade.Id,
            StatisticsSourceFilter.Quick, StatisticsSideFilter.B))!.Total.Wins);
        Assert.Null(await stats.GetBeybladeVersionStatisticsAsync(fixture.Other.Id, fixture.Blade.Id));
        Assert.Equal(3, (await stats.GetOpponentBeybladeStatisticsAsync(fixture.Owner.Id, fixture.Other.Id)).Count);
    }

    private static List<Part> Select(string selection)
    {
        var entries = PartCatalog.Read();
        return selection.Split(',', StringSplitOptions.RemoveEmptyEntries).Select((key, index) =>
        {
            var pair = key.Split(':');
            var entry = entries.Single(x => x.Category == Enum.Parse<PartCategory>(pair[0]) && x.Name == pair[1]);
            return new Part { Id = index + 1, Category = entry.Category, Name = entry.Name, IntegratesRatchet = entry.IntegratesRatchet };
        }).ToList();
    }

    private sealed class Fixture(SqliteConnection connection, AppDbContext db, User owner, User other, Beyblade blade) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;
        public User Owner { get; } = owner;
        public User Other { get; } = other;
        public Beyblade Blade { get; } = blade;
        public async Task<int[]> IdsAsync() => await Db.Parts.Where(x =>
            (x.Category == PartCategory.Blade && x.Name == "時鐘幻象") ||
            (x.Category == PartCategory.Ratchet && x.Name == "4-55") ||
            (x.Category == PartCategory.Bit && x.Name == "S")).OrderBy(x => x.Category).Select(x => x.Id).ToArrayAsync();
        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            Assert.Equal(279, await PartCatalog.ImportAsync(db));
            var owner = new User { Account = "owner", DisplayName = "Owner", PasswordHash = "x" };
            var other = new User { Account = "other", DisplayName = "Other", PasswordHash = "x" };
            var blade = new Beyblade { User = owner, Name = "legacy" };
            db.AddRange(owner, other, blade);
            await db.SaveChangesAsync();
            return new Fixture(connection, db, owner, other, blade);
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
