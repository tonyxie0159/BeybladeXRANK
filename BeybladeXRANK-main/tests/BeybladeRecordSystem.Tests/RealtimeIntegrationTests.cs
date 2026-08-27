using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BeybladeRecordSystem.Tests;

public sealed class RealtimeIntegrationTests(AccountWebApplicationFactory factory)
    : IClassFixture<AccountWebApplicationFactory>
{
    [Fact]
    public async Task QuickInvitation_PushesPrivateNotificationToAuthenticatedInvitee()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var inviterAccount = $"hub-inviter-{suffix}";
        var inviteeAccount = $"hub-invitee-{suffix}";
        const string password = "local signalr integration password";
        int inviterId;
        int inviteeId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
            Assert.True((await auth.RegisterAsync(inviterAccount, password, $"邀請者-{suffix[..6]}")).Succeeded);
            Assert.True((await auth.RegisterAsync(inviteeAccount, password, $"受邀者-{suffix[..6]}")).Succeeded);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            inviterId = await db.Users.Where(x => x.NormalizedAccount == inviterAccount.ToUpperInvariant()).Select(x => x.Id).SingleAsync();
            inviteeId = await db.Users.Where(x => x.NormalizedAccount == inviteeAccount.ToUpperInvariant()).Select(x => x.Id).SingleAsync();
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        var authCookie = await LoginAndGetAuthenticationCookieAsync(client, inviteeAccount, password);
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/realtime"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Headers["Cookie"] = authCookie;
            })
            .Build();
        connection.On<JsonElement>("RealtimeEvent", message =>
        {
            if (message.TryGetProperty("eventType", out var type) && type.GetString() == "notification")
                received.TrySetResult(message);
        });
        await connection.StartAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var quickFlow = scope.ServiceProvider.GetRequiredService<QuickBattleFlowService>();
            Assert.True((await quickFlow.SendInvitationAsync(inviterId, inviteeId)).Succeeded);
        }

        var pushed = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("notification", pushed.GetProperty("eventType").GetString());
        var payload = pushed.GetProperty("payload");
        Assert.Equal("快速對戰邀請", payload.GetProperty("title").GetString());
        Assert.Equal("/Battles/Invitations", payload.GetProperty("targetUrl").GetString());
        await connection.StopAsync();
    }

    private static async Task<string> LoginAndGetAuthenticationCookieAsync(
        HttpClient client,
        string account,
        string password)
    {
        using var loginPage = await client.GetAsync("/Account/Login");
        loginPage.EnsureSuccessStatusCode();
        var html = await loginPage.Content.ReadAsStringAsync();
        var tokenInput = Regex.Match(html, "<input(?=[^>]*name=\"__RequestVerificationToken\")[^>]*>", RegexOptions.IgnoreCase);
        var tokenValue = Regex.Match(tokenInput.Value, "value=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        Assert.True(tokenInput.Success && tokenValue.Success);
        using var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(tokenValue.Groups[1].Value),
            ["Account"] = account,
            ["Password"] = password
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var cookies = response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];
        var authenticationCookie = cookies.Select(x => x.Split(';', 2)[0])
            .SingleOrDefault(x => x.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal));
        Assert.False(string.IsNullOrWhiteSpace(authenticationCookie));
        return authenticationCookie!;
    }
}
