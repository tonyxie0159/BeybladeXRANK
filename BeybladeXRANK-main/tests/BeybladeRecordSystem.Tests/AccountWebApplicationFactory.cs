using BeybladeRecordSystem.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BeybladeRecordSystem.Tests;

public sealed class AccountWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string dataDirectory = Path.Combine(
        Path.GetTempPath(),
        $"beybladexrank-web-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("RuntimeDataDirectory", dataDirectory);
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=unused;Database=unused;Username=unused");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["RuntimeDataDirectory"] = dataDirectory,
                ["ConnectionStrings:DefaultConnection"] = "Host=unused;Database=unused;Username=unused"
            }));
        builder.ConfigureTestServices(services =>
        {
            var registrations = services
                .Where(x => x.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                            x.ServiceType.Name.Contains("IDbContextOptionsConfiguration", StringComparison.Ordinal))
                .ToList();
            foreach (var registration in registrations)
                services.Remove(registration);

            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite($"Data Source={Path.Combine(dataDirectory, "web-tests.db")};Pooling=False"));
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

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
