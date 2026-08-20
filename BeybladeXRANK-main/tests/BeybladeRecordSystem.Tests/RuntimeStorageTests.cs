using BeybladeRecordSystem.Infrastructure;
using Microsoft.Data.Sqlite;

namespace BeybladeRecordSystem.Tests;

public class RuntimeStorageTests
{
    [Fact]
    public void DevelopmentDataDirectory_ResolvesToProjectRootInsteadOfSourceDataFolder()
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), "BeybladeXRANK-main");
        var contentRoot = Path.Combine(repositoryRoot, "src", "BeybladeRecordSystem");

        var resolved = RuntimeStorage.ResolveDataDirectory(contentRoot, "../../data");

        Assert.Equal(Path.GetFullPath(Path.Combine(repositoryRoot, "data")), resolved);
        Assert.NotEqual(Path.GetFullPath(Path.Combine(contentRoot, "Data")), resolved);
    }

    [Fact]
    public void RelativeSqliteDataSource_ResolvesInsideRuntimeDataDirectory()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "beyblade-runtime-data");

        var resolved = RuntimeStorage.ResolveSqliteConnectionString(
            dataDirectory, "Data Source=beyblade.db;Foreign Keys=True");
        var builder = new SqliteConnectionStringBuilder(resolved);

        Assert.Equal(Path.GetFullPath(Path.Combine(dataDirectory, "beyblade.db")), builder.DataSource);
        Assert.True(builder.ForeignKeys);
    }

    [Fact]
    public void AbsoluteAndInMemorySqliteDataSources_AreNotRebased()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "beyblade-runtime-data");
        var absoluteDatabase = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "absolute-beyblade.db"));

        var absolute = new SqliteConnectionStringBuilder(RuntimeStorage.ResolveSqliteConnectionString(
            dataDirectory, $"Data Source={absoluteDatabase}"));
        var memory = new SqliteConnectionStringBuilder(RuntimeStorage.ResolveSqliteConnectionString(
            dataDirectory, "Data Source=:memory:"));

        Assert.Equal(absoluteDatabase, absolute.DataSource);
        Assert.Equal(":memory:", memory.DataSource);
    }
}
