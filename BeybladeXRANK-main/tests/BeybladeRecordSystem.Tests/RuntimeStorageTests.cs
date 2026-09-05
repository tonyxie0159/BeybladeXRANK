using BeybladeRecordSystem.Infrastructure;

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
}
