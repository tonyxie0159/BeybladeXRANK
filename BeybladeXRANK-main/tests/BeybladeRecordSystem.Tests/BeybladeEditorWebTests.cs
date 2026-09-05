using System.Net;
using System.Text.RegularExpressions;
using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BeybladeRecordSystem.Tests;

public class BeybladeEditorWebTests(AccountWebApplicationFactory factory) : IClassFixture<AccountWebApplicationFactory>
{
    [Fact]
    public async Task CreateAndEdit_RequireCompleteOwnedConfiguration_IgnoreClientCommonName()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var account = "editor-" + Guid.NewGuid().ToString("N");
        await PostAsync(client, "/Account/Register", new() { ["Account"] = account, ["DisplayName"] = account, ["Password"] = "editor password" });
        await PostAsync(client, "/Account/Login", new() { ["Account"] = account, ["Password"] = "editor password" });
        int userId, legacyId, otherId;
        int[] ids;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await PartCatalog.ImportAsync(db);
            userId = await db.Users.Where(x => x.Account == account).Select(x => x.Id).SingleAsync();
            var legacy = new Beyblade { UserId = userId, Name = "legacy" };
            var other = new Beyblade { Name = "other", User = new User { Account = "other-" + account, DisplayName = "other-" + account, PasswordHash = "x" } };
            db.AddRange(legacy, other);
            await db.SaveChangesAsync();
            legacyId = legacy.Id;
            otherId = other.Id;
            ids = await db.Parts.Where(x =>
                (x.Category == PartCategory.Blade && x.Name == "鮫鯊狂鱗") ||
                (x.Category == PartCategory.Ratchet && x.Name == "1-50") ||
                (x.Category == PartCategory.Bit && x.Name == "J")).OrderBy(x => x.Category).Select(x => x.Id).ToArrayAsync();
        }
        var createHtml = await client.GetStringAsync("/Beyblades/Create");
        Assert.Contains("data-parts-editor", createHtml);
        Assert.Contains("parts-editor.js", createHtml);
        Assert.Contains("搜尋輔助戰刃", WebUtility.HtmlDecode(createHtml));
        Assert.DoesNotContain("name=\"CommonName\"", createHtml);
        using var noToken = await client.PostAsync("/Beyblades/Create", new FormUrlEncodedContent(new Dictionary<string, string> { ["Name"] = "bad" }));
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);

        using var missing = await PostAsync(client, "/Beyblades/Create", new() { ["Name"] = "new" }, ids.Take(2));
        Assert.Equal(HttpStatusCode.OK, missing.StatusCode);
        Assert.Contains("鮫鯊狂鱗", WebUtility.HtmlDecode(await missing.Content.ReadAsStringAsync()));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Beyblades.AnyAsync(x => x.UserId == userId && x.Name == "new"));
            Assert.False(await db.BeybladeConfigurations.AnyAsync(x => x.Beyblade.UserId == userId));
        }
        using var badBinding = await PostAsync(client, "/Beyblades/Create", new() { ["Name"] = "bad", ["PartIds"] = "garbage" });
        Assert.Equal(HttpStatusCode.OK, badBinding.StatusCode);
        using var saved = await PostAsync(client, "/Beyblades/Create", new() { ["Name"] = "new", ["CommonName"] = "forged", ["ReturnUrl"] = "/Beyblades" }, ids);
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Equal("/Beyblades", saved.Headers.Location?.OriginalString);
        int newId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var blade = await db.Beyblades.WithConfiguration().SingleAsync(x => x.UserId == userId && x.Name == "new");
            newId = blade.Id;
            Assert.Equal("鮫鯊狂鱗1-50J", blade.Configuration!.CommonName);
            Assert.Equal(3, blade.Configuration.Parts.Count);
        }
        var index = WebUtility.HtmlDecode(await client.GetStringAsync("/Beyblades"));
        Assert.Contains("鮫鯊狂鱗1-50J", index);
        Assert.DoesNotContain("forged", index);
        using var missingEdit = await PostAsync(client, $"/Beyblades/Edit/{legacyId}", new() { ["Name"] = "renamed" }, ids.Take(1));
        Assert.Equal(HttpStatusCode.OK, missingEdit.StatusCode);
        using var otherGet = await client.GetAsync($"/Beyblades/Edit/{otherId}");
        Assert.Equal(HttpStatusCode.NotFound, otherGet.StatusCode);
        var token = await TokenAsync(client, $"/Beyblades/Edit/{legacyId}");
        using var otherPost = await client.PostAsync($"/Beyblades/Edit/{otherId}", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["Name"] = "stolen" }));
        Assert.Equal(HttpStatusCode.NotFound, otherPost.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal("legacy", (await db.Beyblades.FindAsync(legacyId))!.Name);
        }
        int[] legacyParts;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            legacyParts = ids.ToArray();
            legacyParts[0] = await db.Parts.Where(x => x.Category == PartCategory.Blade && x.Name == "時鐘幻象").Select(x => x.Id).SingleAsync();
        }
        using var backfill = await PostAsync(client, $"/Beyblades/Edit/{legacyId}", new() { ["Name"] = "renamed", ["Id"] = otherId.ToString() }, legacyParts);
        Assert.Equal(HttpStatusCode.Redirect, backfill.StatusCode);
        using var swap = await PostAsync(client, $"/Beyblades/Edit/{legacyId}", new() { ["Name"] = "changed" }, ids);
        Assert.Equal(HttpStatusCode.OK, swap.StatusCode);
        Assert.Contains("上蓋名稱不同", WebUtility.HtmlDecode(await swap.Content.ReadAsStringAsync()));
        using var rename = await PostAsync(client, $"/Beyblades/Edit/{newId}?handler=Rename", new() { ["Name"] = "my new name", ["CommonName"] = "forged" });
        Assert.Equal(HttpStatusCode.Redirect, rename.StatusCode);
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("other", (await verifyDb.Beyblades.FindAsync(otherId))!.Name);
        Assert.Equal("renamed", (await verifyDb.Beyblades.FindAsync(legacyId))!.Name);
        var renamed = await verifyDb.Beyblades.WithConfiguration().SingleAsync(x => x.Id == newId);
        Assert.Equal("my new name · 鮫鯊狂鱗1-50J", renamed.DisplayName);
        Assert.Equal(2, await verifyDb.BeybladeConfigurations.CountAsync(x => x.Beyblade.UserId == userId));
        using var stats = await client.GetAsync($"/Statistics/Beyblade/{newId}");
        Assert.Equal(HttpStatusCode.OK, stats.StatusCode);
        using var deniedStats = await client.GetAsync($"/Statistics/Beyblade/{otherId}");
        Assert.Equal(HttpStatusCode.NotFound, deniedStats.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, Dictionary<string, string> fields, IEnumerable<int>? ids = null)
    {
        var values = fields.ToList();
        values.Add(new("__RequestVerificationToken", await TokenAsync(client, path)));
        if (ids is not null) values.AddRange(ids.Select(x => new KeyValuePair<string, string>("PartIds", x.ToString())));
        return await client.PostAsync(path, new FormUrlEncodedContent(values));
    }

    private static async Task<string> TokenAsync(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
