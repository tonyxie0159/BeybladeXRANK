using System.Reflection;
using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.DataMigration;
using BeybladeRecordSystem.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

const string LatestLegacyMigration = "20260901161104_RepairLegacyIdentityConflicts";

try
{
    return await RunAsync(args);
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Migration failed: {exception.Message}");
    return 1;
}

static async Task<int> RunAsync(string[] args)
{
    if (args.Contains("--help", StringComparer.Ordinal) || args.Length == 0)
    {
        PrintUsage();
        return args.Length == 0 ? 1 : 0;
    }

    var sourcePath = ReadRequiredOption(args, "--source");
    var targetConnectionString = ReadRequiredOption(args, "--target");
    if (!args.Contains("--confirm-empty-target", StringComparer.Ordinal))
        throw new InvalidOperationException("Pass --confirm-empty-target after verifying that the PostgreSQL target is disposable or empty.");

    sourcePath = Path.GetFullPath(sourcePath);
    if (!File.Exists(sourcePath))
        throw new FileNotFoundException("The SQLite source file does not exist.", sourcePath);

    var sourceConnectionString = new SqliteConnectionStringBuilder
    {
        DataSource = sourcePath,
        Mode = SqliteOpenMode.ReadOnly,
        Pooling = false
    }.ToString();

    await using var sourceConnection = new SqliteConnection(sourceConnectionString);
    await sourceConnection.OpenAsync();
    await ValidateSqliteSourceAsync(sourceConnection);

    var sourceOptions = new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(sourceConnection)
        .Options;
    var targetOptions = new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(targetConnectionString)
        .Options;

    await using var source = new AppDbContext(sourceOptions);
    await using var target = new AppDbContext(targetOptions);

    Console.WriteLine("Reading the SQLite source in read-only mode...");
    var data = await MigrationData.LoadAsync(source);
    data.NormalizeUtcDateTimes();

    await EnsureTargetIsSafeBeforeSchemaChangesAsync(targetConnectionString, target);
    Console.WriteLine("Applying the PostgreSQL schema migration...");
    await target.Database.MigrateAsync();
    await EnsureTargetIsEmptyAsync(target);

    await using var transaction = await target.Database.BeginTransactionAsync();
    try
    {
        await InsertAsync(target, target.Users, data.Users);
        await InsertAsync(target, target.Beyblades, data.Beyblades);
        await InsertAsync(target, target.Tournaments, data.Tournaments);
        await InsertAsync(target, target.TournamentEntries, data.TournamentEntries);
        await InsertAsync(target, target.TournamentEntryMembers, data.TournamentEntryMembers);
        await InsertAsync(target, target.TournamentInvitations, data.TournamentInvitations);
        await InsertAsync(target, target.QuickBattleInvitations, data.QuickBattleInvitations);

        var matchLinks = data.TournamentMatches
            .Select(match => new MatchLinks(match.Id, match.WinnerToMatchId, match.LoserToMatchId))
            .ToList();
        foreach (var match in data.TournamentMatches)
        {
            match.WinnerToMatchId = null;
            match.LoserToMatchId = null;
        }
        await InsertAsync(target, target.TournamentMatches, data.TournamentMatches);
        foreach (var link in matchLinks)
        {
            await target.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "TournamentMatches"
                SET "WinnerToMatchId" = {link.WinnerToMatchId},
                    "LoserToMatchId" = {link.LoserToMatchId}
                WHERE "Id" = {link.Id}
                """);
        }
        foreach (var match in data.TournamentMatches)
        {
            var link = matchLinks.Single(item => item.Id == match.Id);
            match.WinnerToMatchId = link.WinnerToMatchId;
            match.LoserToMatchId = link.LoserToMatchId;
        }

        await InsertAsync(target, target.Battles, data.Battles);
        await InsertAsync(target, target.BattleLineups, data.BattleLineups);
        await InsertAsync(target, target.BattleLineupSelections, data.BattleLineupSelections);
        await InsertAsync(target, target.BattleTeamOrderSelections, data.BattleTeamOrderSelections);
        await InsertAsync(target, target.BattleRounds, data.BattleRounds);
        await InsertAsync(target, target.BattleRoundEvents, data.BattleRoundEvents);
        await InsertAsync(target, target.BattleRoundRevisions, data.BattleRoundRevisions);
        await InsertAsync(target, target.TournamentMatchParticipants, data.TournamentMatchParticipants);
        await InsertAsync(target, target.UserNotifications, data.UserNotifications);

        await ResetIdentitySequencesAsync(target);
        await VerifyCountsAsync(target, data.Counts);
        await VerifyDataAsync(target, data);
        await transaction.CommitAsync();
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }

    Console.WriteLine("Migration completed; all table counts and scalar values match.");
    foreach (var item in data.Counts.OrderBy(item => item.Key, StringComparer.Ordinal))
        Console.WriteLine($"  {item.Key}: {item.Value}");
    return 0;
}

static string ReadRequiredOption(string[] args, string option)
{
    var index = Array.IndexOf(args, option);
    if (index < 0 || index == args.Length - 1 || string.IsNullOrWhiteSpace(args[index + 1]))
        throw new ArgumentException($"Missing required option {option}.");
    return args[index + 1];
}

static void PrintUsage()
{
    Console.WriteLine("""
        Copies a fully migrated BeybladeXRANK SQLite database into an empty PostgreSQL database.

        dotnet run --project tools/BeybladeRecordSystem.DataMigration -- \
          --source <sqlite-backup.db> \
          --target <postgres-connection-string> \
          --confirm-empty-target

        The SQLite file is opened read-only. The PostgreSQL import is transactional and refuses
        to run when any application table already contains rows.
        """);
}

static async Task ValidateSqliteSourceAsync(SqliteConnection connection)
{
    await using (var integrity = connection.CreateCommand())
    {
        integrity.CommandText = "PRAGMA integrity_check";
        var result = Convert.ToString(await integrity.ExecuteScalarAsync());
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"SQLite integrity_check failed: {result}");
    }

    await using (var foreignKeys = connection.CreateCommand())
    {
        foreignKeys.CommandText = "PRAGMA foreign_key_check";
        await using var reader = await foreignKeys.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            throw new InvalidOperationException($"SQLite foreign_key_check found an orphan in table {reader.GetString(0)}.");
    }

    await using var migration = connection.CreateCommand();
    migration.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1";
    var latest = Convert.ToString(await migration.ExecuteScalarAsync());
    if (!string.Equals(latest, LatestLegacyMigration, StringComparison.Ordinal))
        throw new InvalidOperationException(
            $"SQLite source must be upgraded through {LatestLegacyMigration}; found {latest ?? "no migration history"}.");
}

static async Task InsertAsync<TEntity>(AppDbContext target, DbSet<TEntity> set, IReadOnlyCollection<TEntity> rows)
    where TEntity : class
{
    if (rows.Count == 0)
        return;

    set.AddRange(rows);
    await target.SaveChangesAsync();
    target.ChangeTracker.Clear();
}

static async Task EnsureTargetIsEmptyAsync(AppDbContext target)
{
    var counts = await GetTargetCountsAsync(target);
    var populated = counts.Where(item => item.Value != 0).ToList();
    if (populated.Count != 0)
        throw new InvalidOperationException(
            "PostgreSQL target is not empty: " + string.Join(", ", populated.Select(item => $"{item.Key}={item.Value}")));
}

static async Task EnsureTargetIsSafeBeforeSchemaChangesAsync(string connectionString, AppDbContext target)
{
    string[] expectedTables =
    [
        "Users", "Beyblades", "Tournaments", "TournamentEntries", "TournamentEntryMembers",
        "TournamentInvitations", "QuickBattleInvitations", "TournamentMatches", "Battles",
        "BattleLineups", "BattleLineupSelections", "BattleTeamOrderSelections", "BattleRounds",
        "BattleRoundEvents", "BattleRoundRevisions", "TournamentMatchParticipants", "UserNotifications"
    ];

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT table_name
        FROM information_schema.tables
        WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
        ORDER BY table_name
        """;
    var existing = new HashSet<string>(StringComparer.Ordinal);
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
            existing.Add(reader.GetString(0));
    }

    existing.Remove("__EFMigrationsHistory");
    if (existing.Count == 0)
        return;

    var expected = expectedTables.ToHashSet(StringComparer.Ordinal);
    string[] partsTables = ["Parts", "PartSeries", "BeybladeConfigurations", "BeybladeConfigurationParts"];
    var withParts = expected.Concat(partsTables).ToHashSet(StringComparer.Ordinal);
    var unexpected = existing.Except(withParts, StringComparer.Ordinal).ToList();
    var missing = expected.Except(existing, StringComparer.Ordinal).ToList();
    if (unexpected.Count != 0 || missing.Count != 0 || (!existing.SetEquals(expected) && !existing.SetEquals(withParts)))
        throw new InvalidOperationException(
            $"PostgreSQL target schema is not an empty BeybladeXRANK schema. " +
            $"Unexpected tables: {string.Join(", ", unexpected)}; missing tables: {string.Join(", ", missing)}.");

    // Inspect only the validated tables that exist before applying newer migrations.
    // In particular, reject already seeded catalogs instead of treating them as empty.
    foreach (var table in existing)
    {
        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        if (Convert.ToInt64(await countCommand.ExecuteScalarAsync()) != 0)
            throw new InvalidOperationException($"PostgreSQL target is not empty: {table}.");
    }
}

static async Task VerifyCountsAsync(AppDbContext target, IReadOnlyDictionary<string, int> expected)
{
    var actual = await GetTargetCountsAsync(target);
    var mismatches = expected
        .Where(item => !actual.TryGetValue(item.Key, out var count) || count != item.Value)
        .Select(item => $"{item.Key}: expected {item.Value}, actual {actual.GetValueOrDefault(item.Key)}")
        .ToList();
    if (mismatches.Count != 0)
        throw new InvalidOperationException("Row-count verification failed: " + string.Join("; ", mismatches));
}

static async Task VerifyDataAsync(AppDbContext target, MigrationData expected)
{
    await VerifyRowsAsync(expected.Users, await target.Users.AsNoTracking().OrderBy(item => item.Id).ToListAsync());
    await VerifyRowsAsync(expected.Beyblades, await target.Beyblades.AsNoTracking().OrderBy(item => item.Id).ToListAsync());
    await VerifyRowsAsync(expected.Tournaments, await target.Tournaments.AsNoTracking().OrderBy(item => item.Id).ToListAsync());
    await VerifyRowsAsync(expected.TournamentEntries, await target.TournamentEntries.AsNoTracking().OrderBy(item => item.Id).ToListAsync());
    await VerifyRowsAsync(expected.TournamentEntryMembers, await target.TournamentEntryMembers.AsNoTracking().OrderBy(item => item.Id).ToListAsync());
    await VerifyRowsAsync(expected.TournamentInvitations, await target.TournamentInvitations.AsNoTracking().OrderBy(item => item.Id).ToListAsync());
    await VerifyRowsAsync(expected.QuickBattleInvitations, await target.QuickBattleInvitations.AsNoTracking().OrderBy(item => item.Id).ToListAsync());
    await VerifyRowsAsync(expected.TournamentMatches, await target.TournamentMatches.AsNoTracking().OrderBy(item => item.Id).ToListAsync());
    await VerifyRowsAsync(expected.Battles, await target.Battles.AsNoTracking().OrderBy(item => item.Id).ToListAsync());
    await VerifyRowsAsync(expected.BattleLineups, await target.BattleLineups.AsNoTracking().OrderBy(item => item.Id).ToListAsync());
    await VerifyRowsAsync(expected.BattleLineupSelections, await target.BattleLineupSelections.AsNoTracking().OrderBy(item => item.Id).ToListAsync());
    await VerifyRowsAsync(expected.BattleTeamOrderSelections, await target.BattleTeamOrderSelections.AsNoTracking().OrderBy(item => item.Id).ToListAsync());
    await VerifyRowsAsync(expected.BattleRounds, await target.BattleRounds.AsNoTracking().OrderBy(item => item.Id).ToListAsync());
    await VerifyRowsAsync(expected.BattleRoundEvents, await target.BattleRoundEvents.AsNoTracking().OrderBy(item => item.Id).ToListAsync());
    await VerifyRowsAsync(expected.BattleRoundRevisions, await target.BattleRoundRevisions.AsNoTracking().OrderBy(item => item.Id).ToListAsync());
    await VerifyRowsAsync(expected.TournamentMatchParticipants, await target.TournamentMatchParticipants.AsNoTracking().OrderBy(item => item.Id).ToListAsync());
    await VerifyRowsAsync(expected.UserNotifications, await target.UserNotifications.AsNoTracking().OrderBy(item => item.Id).ToListAsync());
}

static Task VerifyRowsAsync<TEntity>(IReadOnlyList<TEntity> expected, IReadOnlyList<TEntity> actual)
    where TEntity : class
{
    if (expected.Count != actual.Count)
        throw new InvalidOperationException($"Content verification failed for {typeof(TEntity).Name}: row counts differ.");

    var properties = typeof(TEntity)
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(property => property.CanRead && IsScalar(property.PropertyType))
        .OrderBy(property => property.Name, StringComparer.Ordinal)
        .ToArray();

    for (var rowIndex = 0; rowIndex < expected.Count; rowIndex++)
    {
        foreach (var property in properties)
        {
            var expectedValue = property.GetValue(expected[rowIndex]);
            var actualValue = property.GetValue(actual[rowIndex]);
            var equal = expectedValue is byte[] expectedBytes && actualValue is byte[] actualBytes
                ? expectedBytes.SequenceEqual(actualBytes)
                : Equals(expectedValue, actualValue);
            if (!equal)
                throw new InvalidOperationException(
                    $"Content verification failed for {typeof(TEntity).Name} row {rowIndex + 1}, property {property.Name}.");
        }
    }

    return Task.CompletedTask;
}

static bool IsScalar(Type type)
{
    type = Nullable.GetUnderlyingType(type) ?? type;
    return type.IsValueType || type == typeof(string) || type == typeof(byte[]);
}

static async Task<Dictionary<string, int>> GetTargetCountsAsync(AppDbContext db) => new(StringComparer.Ordinal)
{
    ["Users"] = await db.Users.CountAsync(),
    ["Beyblades"] = await db.Beyblades.CountAsync(),
    ["Parts"] = await db.Parts.CountAsync(),
    ["PartSeries"] = await db.PartSeries.CountAsync(),
    ["BeybladeConfigurations"] = await db.BeybladeConfigurations.CountAsync(),
    ["BeybladeConfigurationParts"] = await db.BeybladeConfigurationParts.CountAsync(),
    ["Tournaments"] = await db.Tournaments.CountAsync(),
    ["TournamentEntries"] = await db.TournamentEntries.CountAsync(),
    ["TournamentEntryMembers"] = await db.TournamentEntryMembers.CountAsync(),
    ["TournamentInvitations"] = await db.TournamentInvitations.CountAsync(),
    ["QuickBattleInvitations"] = await db.QuickBattleInvitations.CountAsync(),
    ["TournamentMatches"] = await db.TournamentMatches.CountAsync(),
    ["Battles"] = await db.Battles.CountAsync(),
    ["BattleLineups"] = await db.BattleLineups.CountAsync(),
    ["BattleLineupSelections"] = await db.BattleLineupSelections.CountAsync(),
    ["BattleTeamOrderSelections"] = await db.BattleTeamOrderSelections.CountAsync(),
    ["BattleRounds"] = await db.BattleRounds.CountAsync(),
    ["BattleRoundEvents"] = await db.BattleRoundEvents.CountAsync(),
    ["BattleRoundRevisions"] = await db.BattleRoundRevisions.CountAsync(),
    ["TournamentMatchParticipants"] = await db.TournamentMatchParticipants.CountAsync(),
    ["UserNotifications"] = await db.UserNotifications.CountAsync()
};

static async Task ResetIdentitySequencesAsync(AppDbContext target)
{
    string[] tables =
    [
        "Users", "Beyblades", "Tournaments", "TournamentEntries", "TournamentEntryMembers",
        "TournamentInvitations", "QuickBattleInvitations", "TournamentMatches", "Battles",
        "BattleLineups", "BattleLineupSelections", "BattleTeamOrderSelections", "BattleRounds",
        "BattleRoundEvents", "BattleRoundRevisions", "TournamentMatchParticipants", "UserNotifications"
    ];

    foreach (var table in tables)
    {
        var sql = $"""
            SELECT setval(
                pg_get_serial_sequence('"{table}"', 'Id'),
                COALESCE(MAX("Id"), 1),
                COUNT(*) > 0)
            FROM "{table}"
            """;
        await target.Database.ExecuteSqlRawAsync(sql);
    }
}

sealed record MatchLinks(int Id, int? WinnerToMatchId, int? LoserToMatchId);

sealed record MigrationData(
    List<User> Users,
    List<Beyblade> Beyblades,
    List<Tournament> Tournaments,
    List<TournamentEntry> TournamentEntries,
    List<TournamentEntryMember> TournamentEntryMembers,
    List<TournamentInvitation> TournamentInvitations,
    List<QuickBattleInvitation> QuickBattleInvitations,
    List<TournamentMatch> TournamentMatches,
    List<Battle> Battles,
    List<BattleLineup> BattleLineups,
    List<BattleLineupSelection> BattleLineupSelections,
    List<BattleTeamOrderSelection> BattleTeamOrderSelections,
    List<BattleRound> BattleRounds,
    List<BattleRoundEvent> BattleRoundEvents,
    List<BattleRoundRevision> BattleRoundRevisions,
    List<TournamentMatchParticipant> TournamentMatchParticipants,
    List<UserNotification> UserNotifications)
{
    public IReadOnlyDictionary<string, int> Counts => new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["Users"] = Users.Count,
        ["Beyblades"] = Beyblades.Count,
        ["Tournaments"] = Tournaments.Count,
        ["TournamentEntries"] = TournamentEntries.Count,
        ["TournamentEntryMembers"] = TournamentEntryMembers.Count,
        ["TournamentInvitations"] = TournamentInvitations.Count,
        ["QuickBattleInvitations"] = QuickBattleInvitations.Count,
        ["TournamentMatches"] = TournamentMatches.Count,
        ["Battles"] = Battles.Count,
        ["BattleLineups"] = BattleLineups.Count,
        ["BattleLineupSelections"] = BattleLineupSelections.Count,
        ["BattleTeamOrderSelections"] = BattleTeamOrderSelections.Count,
        ["BattleRounds"] = BattleRounds.Count,
        ["BattleRoundEvents"] = BattleRoundEvents.Count,
        ["BattleRoundRevisions"] = BattleRoundRevisions.Count,
        ["TournamentMatchParticipants"] = TournamentMatchParticipants.Count,
        ["UserNotifications"] = UserNotifications.Count
    };

    public static async Task<MigrationData> LoadAsync(AppDbContext db) => new(
        await db.Users.AsNoTracking().OrderBy(item => item.Id).ToListAsync(),
        await LegacyLineupReader.ReadBeybladesAsync(db),
        await db.Tournaments.AsNoTracking().OrderBy(item => item.Id).ToListAsync(),
        await db.TournamentEntries.AsNoTracking().OrderBy(item => item.Id).ToListAsync(),
        await db.TournamentEntryMembers.AsNoTracking().OrderBy(item => item.Id).ToListAsync(),
        await db.TournamentInvitations.AsNoTracking().OrderBy(item => item.Id).ToListAsync(),
        await db.QuickBattleInvitations.AsNoTracking().OrderBy(item => item.Id).ToListAsync(),
        await db.TournamentMatches.AsNoTracking().OrderBy(item => item.Id).ToListAsync(),
        await db.Battles.AsNoTracking().OrderBy(item => item.Id).ToListAsync(),
        await LegacyLineupReader.ReadLineupsAsync(db),
        await LegacyLineupReader.ReadSelectionsAsync(db),
        await db.BattleTeamOrderSelections.AsNoTracking().OrderBy(item => item.Id).ToListAsync(),
        await db.BattleRounds.AsNoTracking().OrderBy(item => item.Id).ToListAsync(),
        await db.BattleRoundEvents.AsNoTracking().OrderBy(item => item.Id).ToListAsync(),
        await db.BattleRoundRevisions.AsNoTracking().OrderBy(item => item.Id).ToListAsync(),
        await db.TournamentMatchParticipants.AsNoTracking().OrderBy(item => item.Id).ToListAsync(),
        await db.UserNotifications.AsNoTracking().OrderBy(item => item.Id).ToListAsync());

    public void NormalizeUtcDateTimes()
    {
        foreach (var row in AllRows())
        {
            foreach (var property in row.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || !property.CanWrite)
                    continue;
                if (property.PropertyType == typeof(DateTime) && property.GetValue(row) is DateTime value)
                    property.SetValue(row, AsUtc(value));
                else if (property.PropertyType == typeof(DateTime?) && property.GetValue(row) is DateTime nullableValue)
                    property.SetValue(row, (DateTime?)AsUtc(nullableValue));
            }
        }
    }

    private IEnumerable<object> AllRows() =>
        Users.Cast<object>()
            .Concat(Beyblades).Concat(Tournaments).Concat(TournamentEntries)
            .Concat(TournamentEntryMembers).Concat(TournamentInvitations).Concat(QuickBattleInvitations)
            .Concat(TournamentMatches).Concat(Battles).Concat(BattleLineups)
            .Concat(BattleLineupSelections).Concat(BattleTeamOrderSelections).Concat(BattleRounds)
            .Concat(BattleRoundEvents).Concat(BattleRoundRevisions)
            .Concat(TournamentMatchParticipants).Concat(UserNotifications);

    private static DateTime AsUtc(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        // PostgreSQL timestamps have microsecond precision, while .NET and SQLite can
        // retain 100-nanosecond ticks. Normalize before insertion so verification is exact.
        return new DateTime(
            utc.Ticks - utc.Ticks % TimeSpan.TicksPerMicrosecond,
            DateTimeKind.Utc);
    }
}
