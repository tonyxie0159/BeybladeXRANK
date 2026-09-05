using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Realtime;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Add services to the container.
builder.Services.AddRazorPages().AddMvcOptions(options => options.Filters.Add<TournamentRealtimePageFilter>());
builder.Services.AddSignalR();
var dataDirectory = RuntimeStorage.ResolveDataDirectory(
    builder.Environment.ContentRootPath,
    builder.Configuration["RuntimeDataDirectory"]);
Directory.CreateDirectory(dataDirectory);
var databaseConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(databaseConnectionString))
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")))
    .SetApplicationName("BeybladeRecordSystem");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(databaseConnectionString));
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<BeybladeService>();
builder.Services.AddScoped<BeybladeConfigurationService>();
builder.Services.AddScoped<BattleService>();
builder.Services.AddScoped<QuickBattleFlowService>();
builder.Services.AddScoped<StatisticsService>();
builder.Services.AddScoped<TournamentService>();
builder.Services.AddScoped<TournamentMatchService>();
builder.Services.AddScoped<TournamentProgressionService>();
builder.Services.AddScoped<TournamentStandingsService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddSingleton<IRealtimePublisher, RealtimePublisher>();
builder.Services.AddScoped<TournamentRealtimePageFilter>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();
app.MapHub<RealtimeHub>("/hubs/realtime");

if (args.Contains("--migrate", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await PartCatalog.ImportAsync(db);
    return;
}

if (args.Contains("--import-parts", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var added = await PartCatalog.ImportAsync(db);
    app.Logger.LogInformation("Parts catalog imported: {AddedCount} new parts.", added);
    return;
}

await app.RunAsync();

public partial class Program { }
