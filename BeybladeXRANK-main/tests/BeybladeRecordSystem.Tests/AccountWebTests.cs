using System.Net;
using System.Text.RegularExpressions;
using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BeybladeRecordSystem.Tests;

public sealed partial class AccountWebTests : IClassFixture<AccountWebApplicationFactory>
{
    private readonly AccountWebApplicationFactory factory;

    public AccountWebTests(AccountWebApplicationFactory factory) => this.factory = factory;

    [Fact]
    public async Task ProtectedPage_RedirectsAnonymousUserToLogin()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/Beyblades");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        AssertRedirectPath(response, "/Account/Login");
    }

    [Fact]
    public async Task AccountLifecycle_UsesAntiforgeryAndPreservesUserOwnership()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var suffix = Guid.NewGuid().ToString("N");
        var account = $"web-{suffix}";
        const string password = "correct horse battery staple";
        var registerToken = await GetAntiforgeryTokenAsync(client, "/Account/Register");
        using var registerResponse = await client.PostAsync("/Account/Register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = registerToken,
            ["Account"] = account,
            ["Password"] = password,
            ["DisplayName"] = $"Web User {suffix[..8]}"
        }));

        Assert.Equal(HttpStatusCode.Redirect, registerResponse.StatusCode);
        Assert.Equal("/Account/Login", registerResponse.Headers.Location?.OriginalString);

        string passwordHash;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(x => x.Account == account);
            Assert.NotEqual(password, user.PasswordHash);
            Assert.DoesNotContain(password, user.PasswordHash, StringComparison.Ordinal);
            passwordHash = user.PasswordHash;
        }

        var loginToken = await GetAntiforgeryTokenAsync(client, "/Account/Login");
        using var loginResponse = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = loginToken,
            ["Account"] = account,
            ["Password"] = password
        }));

        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.Equal("/", loginResponse.Headers.Location?.OriginalString);

        using var homeResponse = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, homeResponse.StatusCode);
        var homeHtml = await homeResponse.Content.ReadAsStringAsync();
        Assert.Contains("id=\"notificationBell\"", homeHtml, StringComparison.Ordinal);
        Assert.Contains("id=\"notificationToastRegion\"", homeHtml, StringComparison.Ordinal);
        Assert.Contains("/lib/signalr/dist/browser/signalr.min.js", homeHtml, StringComparison.Ordinal);
        Assert.Contains("/js/notifications.js", homeHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("action-number", homeHtml, StringComparison.Ordinal);
        Assert.True(
            homeHtml.IndexOf("id=\"notificationBell\"", StringComparison.Ordinal) <
            homeHtml.IndexOf("id=\"primaryNavigation\"", StringComparison.Ordinal),
            "通知鈴鐺必須位於可收合導覽之外，手機版才能持續顯示。");

        using var settingsResponse = await client.GetAsync("/Account/Settings");
        Assert.Equal(HttpStatusCode.OK, settingsResponse.StatusCode);
        var settingsHtml = await settingsResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(password, settingsHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(passwordHash, settingsHtml, StringComparison.Ordinal);

        var otherAccount = $"other-{suffix}";
        using (var otherClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        }))
        {
            var otherRegisterToken = await GetAntiforgeryTokenAsync(otherClient, "/Account/Register");
            using var otherRegisterResponse = await otherClient.PostAsync("/Account/Register", Form(
                ("__RequestVerificationToken", otherRegisterToken),
                ("Account", otherAccount),
                ("Password", password),
                ("DisplayName", "Other User")));
            Assert.Equal(HttpStatusCode.Redirect, otherRegisterResponse.StatusCode);
        }

        var settingsToken = GetAntiforgeryToken(settingsHtml, "/Account/Settings");
        using var settingsPostResponse = await client.PostAsync("/Account/Settings", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = settingsToken,
            ["DisplayName"] = "Updated Web User"
        }));

        Assert.Equal(HttpStatusCode.Redirect, settingsPostResponse.StatusCode);
        Assert.Equal("/Account/Settings", settingsPostResponse.Headers.Location?.OriginalString);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal("Updated Web User", (await db.Users.SingleAsync(x => x.Account == account)).DisplayName);
            Assert.Equal("Other User", (await db.Users.SingleAsync(x => x.Account == otherAccount)).DisplayName);
        }

        var logoutToken = await GetAntiforgeryTokenAsync(client, "/Account/Logout");
        using var logoutResponse = await client.PostAsync("/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = logoutToken
        }));

        Assert.Equal(HttpStatusCode.Redirect, logoutResponse.StatusCode);
        Assert.Equal("/Account/Login", logoutResponse.Headers.Location?.OriginalString);
        using var afterLogout = await client.GetAsync("/Account/Settings");
        Assert.Equal(HttpStatusCode.Redirect, afterLogout.StatusCode);
        AssertRedirectPath(afterLogout, "/Account/Login");
    }

    [Fact]
    public async Task Register_WithoutAntiforgeryToken_IsRejectedWithoutCreatingUser()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var account = $"missing-token-{Guid.NewGuid():N}";

        using var response = await client.PostAsync("/Account/Register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Account"] = account,
            ["Password"] = "not accepted without a token",
            ["DisplayName"] = "Rejected User"
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Users.AnyAsync(x => x.Account == account));
    }

    [Fact]
    public async Task Settings_WithoutAntiforgeryToken_IsRejectedWithoutChangingUser()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        var account = $"settings-token-{Guid.NewGuid():N}";
        const string originalDisplayName = "Original Settings User";
        const string password = "settings antiforgery password";
        await RegisterAsync(client, account, password, originalDisplayName);
        await LoginAsync(client, account, password);

        using var settingsResponse = await client.PostAsync("/Account/Settings", Form(
            ("DisplayName", "Must Not Be Applied")));

        Assert.Equal(HttpStatusCode.BadRequest, settingsResponse.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(originalDisplayName, (await db.Users.SingleAsync(x => x.Account == account)).DisplayName);
    }

    [Fact]
    public async Task Logout_WithoutAntiforgeryToken_IsRejectedWithoutEndingSession()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        var account = $"logout-token-{Guid.NewGuid():N}";
        const string password = "logout antiforgery password";
        await RegisterAsync(client, account, password, "Logout Token User");
        await LoginAsync(client, account, password);

        using var logoutResponse = await client.PostAsync("/Account/Logout", Form());

        Assert.Equal(HttpStatusCode.BadRequest, logoutResponse.StatusCode);
        using var protectedResponse = await client.GetAsync("/Account/Settings");
        Assert.Equal(HttpStatusCode.OK, protectedResponse.StatusCode);
    }

    [Fact]
    public async Task Login_WithInvalidPassword_DoesNotAuthenticateOrExposeCredentials()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        var account = $"invalid-login-{Guid.NewGuid():N}";
        const string password = "valid account password";
        await RegisterAsync(client, account, password, "Invalid Login Test");

        string passwordHash;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            passwordHash = (await db.Users.SingleAsync(x => x.Account == account)).PasswordHash;
        }

        const string invalidPassword = "definitely the wrong password";
        var loginToken = await GetAntiforgeryTokenAsync(client, "/Account/Login");
        using var loginResponse = await client.PostAsync("/Account/Login", Form(
            ("__RequestVerificationToken", loginToken),
            ("Account", account),
            ("Password", invalidPassword)));

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var html = await loginResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(password, html, StringComparison.Ordinal);
        Assert.DoesNotContain(invalidPassword, html, StringComparison.Ordinal);
        Assert.DoesNotContain(passwordHash, html, StringComparison.Ordinal);

        using var protectedResponse = await client.GetAsync("/Account/Settings");
        Assert.Equal(HttpStatusCode.Redirect, protectedResponse.StatusCode);
        AssertRedirectPath(protectedResponse, "/Account/Login");
    }

    [Fact]
    public async Task QuickBattlePage_RendersMobileFriendlySideScoreboardAndCollapsedManagementActions()
    {
        using var firstClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        using var secondClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        var suffix = Guid.NewGuid().ToString("N");
        var firstAccount = $"battle-a-{suffix}";
        var secondAccount = $"battle-b-{suffix}";
        const string password = "local integration test password";
        await RegisterAsync(firstClient, firstAccount, password, $"藍方-{suffix[..6]}");
        await RegisterAsync(secondClient, secondAccount, password, $"紅方-{suffix[..6]}");
        await LoginAsync(firstClient, firstAccount, password);
        await LoginAsync(secondClient, secondAccount, password);

        int battleId;
        int firstUserId;
        string firstDisplayName;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var quickFlow = scope.ServiceProvider.GetRequiredService<QuickBattleFlowService>();
            var battleService = scope.ServiceProvider.GetRequiredService<BattleService>();
            var first = await db.Users.SingleAsync(x => x.NormalizedAccount == firstAccount.ToUpperInvariant());
            var second = await db.Users.SingleAsync(x => x.NormalizedAccount == secondAccount.ToUpperInvariant());
            firstUserId = first.Id;
            firstDisplayName = first.DisplayName;
            var now = DateTime.UtcNow;
            var firstBlades = Enumerable.Range(1, 3).Select(index => new Beyblade
            {
                UserId = first.Id,
                Name = $"藍方陀螺 {index}",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            }).ToList();
            var secondBlades = Enumerable.Range(1, 3).Select(index => new Beyblade
            {
                UserId = second.Id,
                Name = $"紅方陀螺 {index}",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            }).ToList();
            db.Beyblades.AddRange(firstBlades.Concat(secondBlades));
            await db.SaveChangesAsync();

            var invitation = await quickFlow.SendInvitationAsync(first.Id, second.Id);
            Assert.True(invitation.Succeeded);
            var accepted = await quickFlow.AcceptInvitationAsync(invitation.Value!.Id, second.Id);
            Assert.True(accepted.Succeeded);
            battleId = accepted.Value;
            Assert.True((await quickFlow.SubmitLineupAsync(battleId, first.Id, firstBlades.Select(x => x.Id).ToList())).Succeeded);
            Assert.True((await quickFlow.SubmitLineupAsync(battleId, second.Id, secondBlades.Select(x => x.Id).ToList())).Succeeded);
            Assert.True((await quickFlow.ConfirmLineupAsync(battleId, first.Id)).Succeeded);
            Assert.True((await quickFlow.ConfirmLineupAsync(battleId, second.Id)).Succeeded);
            Assert.True((await battleService.AssignSidesAsync(battleId, first.Id, BattleSide.B)).Succeeded);
            Assert.True((await battleService.StartBattleAsync(battleId, first.Id)).Succeeded);
        }

        using var response = await firstClient.GetAsync($"/Battles/Battle/{battleId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("class=\"score-side side-b\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"score-side side-x\"", html, StringComparison.Ordinal);
        Assert.Contains("B Side", html, StringComparison.Ordinal);
        Assert.Contains("X Side", html, StringComparison.Ordinal);
        Assert.Contains("失誤：0", html, StringComparison.Ordinal);
        Assert.Contains("勝利方式", html, StringComparison.Ordinal);
        Assert.Contains("對戰紀錄", html, StringComparison.Ordinal);
        Assert.Contains("判決修改", html, StringComparison.Ordinal);
        Assert.Contains("對戰處理", html, StringComparison.Ordinal);
        Assert.Contains("<details", html, StringComparison.OrdinalIgnoreCase);

        using var historyResponse = await firstClient.GetAsync($"/Battles/History/{battleId}");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var historyHtml = await historyResponse.Content.ReadAsStringAsync();
        Assert.Single(Regex.Matches(historyHtml, "回到比賽"));

        await using (var finishScope = factory.Services.CreateAsyncScope())
        {
            var db = finishScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var battleService = finishScope.ServiceProvider.GetRequiredService<BattleService>();
            var firstRound = await db.BattleRounds.SingleAsync(x => x.BattleId == battleId && x.Status == BattleRoundStatus.InProgress);
            Assert.True((await battleService.RecordAndCompleteRoundAsync(
                battleId, firstRound.Id, firstUserId, firstUserId, ResultType.Extreme)).Succeeded);
            var secondRound = await db.BattleRounds.SingleAsync(x => x.BattleId == battleId && x.Status == BattleRoundStatus.InProgress);
            Assert.True((await battleService.RecordAndCompleteRoundAsync(
                battleId, secondRound.Id, firstUserId, firstUserId, ResultType.SpinFinish)).Succeeded);
            Assert.True((await battleService.FinishBattleAsync(battleId, firstUserId)).Succeeded);
        }

        using var staleBattleResponse = await firstClient.GetAsync($"/Battles/Battle/{battleId}");
        Assert.Equal(HttpStatusCode.Redirect, staleBattleResponse.StatusCode);
        Assert.Equal($"/Battles/Details/{battleId}", staleBattleResponse.Headers.Location?.OriginalString);

        using var resultResponse = await firstClient.GetAsync($"/Battles/Details/{battleId}");
        Assert.Equal(HttpStatusCode.OK, resultResponse.StatusCode);
        var resultHtml = WebUtility.HtmlDecode(await resultResponse.Content.ReadAsStringAsync());
        Assert.Contains("對戰結算", resultHtml, StringComparison.Ordinal);
        Assert.Contains("勝利玩家", resultHtml, StringComparison.Ordinal);
        Assert.Contains(firstDisplayName, resultHtml, StringComparison.Ordinal);
        Assert.Contains("最終比分", resultHtml, StringComparison.Ordinal);
        Assert.Contains("回到首頁", resultHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Register_WithDuplicateAccount_DoesNotEchoSubmittedPassword()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        var account = $"duplicate-{Guid.NewGuid():N}";
        await RegisterAsync(client, account, "first account password", "First Account");

        string originalPasswordHash;
        await using (var hashScope = factory.Services.CreateAsyncScope())
        {
            var hashDb = hashScope.ServiceProvider.GetRequiredService<AppDbContext>();
            originalPasswordHash = (await hashDb.Users.SingleAsync(x => x.Account == account)).PasswordHash;
        }

        const string rejectedPassword = "password that must not be echoed";
        var duplicateToken = await GetAntiforgeryTokenAsync(client, "/Account/Register");
        using var duplicateResponse = await client.PostAsync("/Account/Register", Form(
            ("__RequestVerificationToken", duplicateToken),
            ("Account", account),
            ("Password", rejectedPassword),
            ("DisplayName", "Duplicate Account")));

        Assert.Equal(HttpStatusCode.OK, duplicateResponse.StatusCode);
        var html = await duplicateResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(rejectedPassword, html, StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(x => x.Account == account));
        var persistedUser = await db.Users.SingleAsync(x => x.Account == account);
        Assert.Equal("First Account", persistedUser.DisplayName);
        Assert.Equal(originalPasswordHash, persistedUser.PasswordHash);
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values) =>
        new(values.Select(value => new KeyValuePair<string, string>(value.Key, value.Value)));

    private static void AssertRedirectPath(HttpResponseMessage response, string expectedPath)
    {
        var redirect = Assert.IsType<Uri>(response.Headers.Location);
        var location = redirect.IsAbsoluteUri
            ? redirect
            : new Uri(new Uri("http://localhost"), redirect);
        Assert.Equal(expectedPath, location.AbsolutePath);
    }

    private static async Task RegisterAsync(
        HttpClient client,
        string account,
        string password,
        string displayName)
    {
        var token = await GetAntiforgeryTokenAsync(client, "/Account/Register");
        using var response = await client.PostAsync("/Account/Register", Form(
            ("__RequestVerificationToken", token),
            ("Account", account),
            ("Password", password),
            ("DisplayName", displayName)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.OriginalString);
    }

    private static async Task LoginAsync(HttpClient client, string account, string password)
    {
        var token = await GetAntiforgeryTokenAsync(client, "/Account/Login");
        using var response = await client.PostAsync("/Account/Login", Form(
            ("__RequestVerificationToken", token),
            ("Account", account),
            ("Password", password)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        return GetAntiforgeryToken(html, path);
    }

    private static string GetAntiforgeryToken(string html, string path)
    {
        var input = AntiforgeryInputRegex().Match(html);
        var value = ValueAttributeRegex().Match(input.Value);
        Assert.True(input.Success && value.Success, $"No antiforgery token was rendered by {path}.");
        return WebUtility.HtmlDecode(value.Groups[1].Value);
    }

    [GeneratedRegex("<input(?=[^>]*name=\"__RequestVerificationToken\")[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryInputRegex();

    [GeneratedRegex("value=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex ValueAttributeRegex();
}
