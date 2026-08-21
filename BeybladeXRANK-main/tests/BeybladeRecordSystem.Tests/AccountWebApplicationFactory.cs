using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BeybladeRecordSystem.Tests;

public sealed class AccountWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string dataDirectory = Path.Combine(
        Path.GetTempPath(),
        $"beybladexrank-web-{Guid.NewGuid():N}");

    public string DataDirectory => dataDirectory;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("RuntimeDataDirectory", dataDirectory);
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Data Source=web-tests.db;Pooling=False");
    }

    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
                Directory.Delete(dataDirectory, recursive: true);
        }
    }
}
