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
}
