using System.Net;
using System.Text.RegularExpressions;
using BeybladeRecordSystem.Data;
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
    public async Task RuntimeData_IsIsolatedInsideFactoryTemporaryDirectory()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/Account/Login");
        response.EnsureSuccessStatusCode();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var expectedDatabasePath = Path.GetFullPath(
            Path.Combine(factory.DataDirectory, "web-tests.db"));
        var actualDatabasePath = Path.GetFullPath(db.Database.GetDbConnection().DataSource);
        Assert.Equal(expectedDatabasePath, actualDatabasePath);

        var keyDirectory = Path.Combine(factory.DataDirectory, "keys");
        Assert.NotEmpty(Directory.GetFiles(keyDirectory, "key-*.xml"));
    }

    [Fact]
    public async Task MobileNavigationToggle_ProvidesLabelsForCollapsedAndExpandedStates()
    {
        using var client = factory.CreateClient();

        using var pageResponse = await client.GetAsync("/");
        pageResponse.EnsureSuccessStatusCode();
        var html = await pageResponse.Content.ReadAsStringAsync();
        Assert.Contains("data-collapsed-label=\"開啟導覽選單\"", html, StringComparison.Ordinal);
        Assert.Contains("data-expanded-label=\"關閉導覽選單\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"false\" aria-label=\"開啟導覽選單\"", html, StringComparison.Ordinal);

        using var scriptResponse = await client.GetAsync("/js/site.js");
        scriptResponse.EnsureSuccessStatusCode();
        var script = await scriptResponse.Content.ReadAsStringAsync();
        Assert.Contains("show.bs.collapse", script, StringComparison.Ordinal);
        Assert.Contains("hide.bs.collapse", script, StringComparison.Ordinal);
        Assert.Contains("updateNavigationToggleLabel", script, StringComparison.Ordinal);
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
