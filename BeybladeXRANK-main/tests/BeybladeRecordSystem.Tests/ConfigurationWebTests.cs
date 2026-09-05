using System.Net;
using System.Text.RegularExpressions;
using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BeybladeRecordSystem.Tests;

public class ConfigurationWebTests(AccountWebApplicationFactory factory) : IClassFixture<AccountWebApplicationFactory>
{
    [Fact]
    public async Task ConfigurationPage_RequiresOwnerAndAntiforgery_AndAllowsOneCompleteSubmission()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var anonymous = await client.GetAsync("/Beyblades/Configuration/1");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        var suffix = Guid.NewGuid().ToString("N");
        var account = $"parts-{suffix}";
        const string password = "parts regression password";
        var token = await TokenAsync(client, "/Account/Register");
        using var registered = await client.PostAsync("/Account/Register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["Account"] = account,
            ["DisplayName"] = $"Parts {suffix[..8]}", ["Password"] = password
        }));
        Assert.Equal(HttpStatusCode.Redirect, registered.StatusCode);
        token = await TokenAsync(client, "/Account/Login");
        using var loggedIn = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["Account"] = account, ["Password"] = password
        }));
        Assert.Equal(HttpStatusCode.Redirect, loggedIn.StatusCode);
        int bladeId, otherBladeId;
        int[] ids;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await PartCatalog.ImportAsync(db);
            var owner = await db.Users.SingleAsync(x => x.Account == account);
            var other = new User { Account = $"other-{suffix}", DisplayName = $"Other {suffix[..8]}", PasswordHash = "x" };
            var blade = new Beyblade { UserId = owner.Id, Name = $"owned-{suffix}" };
            var otherBlade = new Beyblade { User = other, Name = "private configuration" };
            db.AddRange(other, blade, otherBlade);
            await db.SaveChangesAsync();
            bladeId = blade.Id;
            otherBladeId = otherBlade.Id;
            ids = await db.Parts.Where(x =>
                (x.Category == PartCategory.Blade && x.Name == "時鐘幻象") ||
                (x.Category == PartCategory.Ratchet && x.Name == "4-55") ||
                (x.Category == PartCategory.Bit && x.Name == "S")).Select(x => x.Id).ToArrayAsync();
        }
        var url = $"/Beyblades/Configuration/{bladeId}";
        using var denied = await client.GetAsync($"/Beyblades/Configuration/{otherBladeId}");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var noToken = await client.PostAsync(url, new FormUrlEncodedContent(ids.Select(x => new KeyValuePair<string, string>("PartIds", x.ToString()))));
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        token = await TokenAsync(client, url);
        var form = ids.Select(x => new KeyValuePair<string, string>("PartIds", x.ToString())).ToList();
        form.Add(new("__RequestVerificationToken", token));
        using var otherPost = await client.PostAsync($"/Beyblades/Configuration/{otherBladeId}", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.NotFound, otherPost.StatusCode);
        using var saved = await client.PostAsync(url, new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        using var repeated = await client.PostAsync(url, new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, repeated.StatusCode);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync(url));
        Assert.Contains("時鐘幻象", html);
        Assert.Contains("編輯／新增版本", html);
        Assert.DoesNotContain("保存完整配置", html);
        await using var verify = factory.Services.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await verifyDb.BeybladeConfigurations.Where(x => x.BeybladeId == bladeId).ToListAsync());
        Assert.False(await verifyDb.BeybladeConfigurations.AnyAsync(x => x.BeybladeId == otherBladeId));
    }

    private static async Task<string> TokenAsync(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
