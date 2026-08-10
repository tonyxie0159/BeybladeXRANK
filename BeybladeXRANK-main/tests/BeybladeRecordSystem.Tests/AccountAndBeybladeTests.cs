using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Tests;

public class AccountAndBeybladeTests
{
    [Fact]
    public async Task Register_StoresHashedPassword_RejectsDuplicate_AndCanLogin()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var auth = new AuthService(fixture.Db);

        Assert.True((await auth.RegisterAsync("player-a", "secret-password", "玩家 A")).Succeeded);
        Assert.False((await auth.RegisterAsync("player-a", "another-password", "其他玩家")).Succeeded);
        var user = await fixture.Db.Users.SingleAsync();

        Assert.NotEqual("secret-password", user.PasswordHash);
        Assert.NotNull(await auth.LoginAsync("player-a", "secret-password"));
        Assert.Null(await auth.LoginAsync("player-a", "wrong-password"));
    }

    [Fact]
    public async Task BeybladeCrud_UsesSoftDelete_AndEnforcesNamesPerUser()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var user = new User { Account = "player-a", PasswordHash = "hash", DisplayName = "玩家 A", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        fixture.Db.Users.Add(user);
        await fixture.Db.SaveChangesAsync();
        var service = new BeybladeService(fixture.Db);

        Assert.True((await service.CreateAsync(user.Id, "Phoenix")).Succeeded);
        Assert.False((await service.CreateAsync(user.Id, "Phoenix")).Succeeded);
        var blade = (await service.GetMyBeybladesAsync(user.Id)).Single();
        Assert.True((await service.RenameAsync(user.Id, blade.Id, "Dran")).Succeeded);
        Assert.True((await service.DeleteAsync(user.Id, blade.Id)).Succeeded);

        Assert.Empty(await service.GetMyBeybladesAsync(user.Id));
        Assert.True((await fixture.Db.Beyblades.FindAsync(blade.Id))!.IsDeleted);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public AppDbContext Db { get; }
        private TestDatabase(SqliteConnection connection, AppDbContext db) { _connection = connection; Db = db; }
        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            return new TestDatabase(connection, db);
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await _connection.DisposeAsync(); }
    }
}
