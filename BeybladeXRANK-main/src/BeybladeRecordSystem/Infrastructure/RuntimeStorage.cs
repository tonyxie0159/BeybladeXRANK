using Microsoft.Data.Sqlite;

namespace BeybladeRecordSystem.Infrastructure;

public static class RuntimeStorage
{
    public static string ResolveDataDirectory(string contentRootPath, string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(contentRootPath))
            throw new ArgumentException("Content root path is required.", nameof(contentRootPath));

        var relativeOrAbsolutePath = string.IsNullOrWhiteSpace(configuredPath)
            ? "data"
            : configuredPath.Trim();
        return Path.GetFullPath(relativeOrAbsolutePath, Path.GetFullPath(contentRootPath));
    }

    public static string ResolveSqliteConnectionString(string dataDirectory, string? configuredConnectionString)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
            throw new ArgumentException("Runtime data directory is required.", nameof(dataDirectory));
        if (string.IsNullOrWhiteSpace(configuredConnectionString))
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

        var builder = new SqliteConnectionStringBuilder(configuredConnectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource))
            throw new InvalidOperationException("SQLite Data Source is required.");
        if (builder.DataSource != ":memory:" && !Path.IsPathRooted(builder.DataSource))
            builder.DataSource = Path.GetFullPath(builder.DataSource, Path.GetFullPath(dataDirectory));
        return builder.ToString();
    }
}
