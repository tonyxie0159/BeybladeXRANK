using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Domain.Tournaments;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BeybladeRecordSystem.Tests;

public sealed class KnownIssueWebTests(AccountWebApplicationFactory factory)
    : IClassFixture<AccountWebApplicationFactory>
{
    [Fact]
    public async Task RevisePage_PrefillsCurrentEffectiveWinnerAndResultType()
    {
        using var firstClient = CreateClient();
        using var secondClient = CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var firstAccount = $"revise-first-{suffix}";
        var secondAccount = $"revise-second-{suffix}";
        const string password = "revision form regression password";
        await RegisterAndLoginAsync(firstClient, firstAccount, password, $"修訂甲-{suffix[..6]}");
        await RegisterAndLoginAsync(secondClient, secondAccount, password, $"修訂乙-{suffix[..6]}");

        int battleId;
        int firstUserId;
        int firstRoundId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var flow = scope.ServiceProvider.GetRequiredService<QuickBattleFlowService>();
            var battles = scope.ServiceProvider.GetRequiredService<BattleService>();
            var first = await db.Users.SingleAsync(x => x.NormalizedAccount == firstAccount.ToUpperInvariant());
            var second = await db.Users.SingleAsync(x => x.NormalizedAccount == secondAccount.ToUpperInvariant());
            firstUserId = first.Id;
            var now = DateTime.UtcNow;
            var firstBlades = Enumerable.Range(1, 3).Select(index => new Beyblade
            {
                UserId = first.Id,
                Name = $"修訂甲陀螺-{suffix[..6]}-{index}",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            }).ToList();
            var secondBlades = Enumerable.Range(1, 3).Select(index => new Beyblade
            {
                UserId = second.Id,
                Name = $"修訂乙陀螺-{suffix[..6]}-{index}",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            }).ToList();
            db.Beyblades.AddRange(firstBlades.Concat(secondBlades));
            await db.SaveChangesAsync();

            var invitation = (await flow.SendInvitationAsync(first.Id, second.Id)).Value!;
            battleId = (await flow.AcceptInvitationAsync(invitation.Id, second.Id)).Value;
            Assert.True((await flow.SubmitLineupAsync(battleId, first.Id, firstBlades.Select(x => x.Id).ToList())).Succeeded);
            Assert.True((await flow.SubmitLineupAsync(battleId, second.Id, secondBlades.Select(x => x.Id).ToList())).Succeeded);
            Assert.True((await flow.ConfirmLineupAsync(battleId, first.Id)).Succeeded);
            Assert.True((await flow.ConfirmLineupAsync(battleId, second.Id)).Succeeded);
            Assert.True((await battles.AssignSidesAsync(battleId, first.Id, BattleSide.B)).Succeeded);
            var firstRound = (await battles.StartBattleAsync(battleId, first.Id)).Value!;
            firstRoundId = firstRound.Id;
            Assert.True((await battles.RecordAndCompleteRoundAsync(
                battleId, firstRound.Id, first.Id, first.Id, ResultType.Extreme)).Succeeded);
        }

        using var response = await firstClient.GetAsync($"/Battles/Revise/{battleId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Matches($"<option[^>]+value=\"{firstUserId}\"[^>]+selected(?:=\"selected\")?", html);
        var resultSelect = Regex.Match(
            html,
            $"<select[^>]+id=\"result-type-{firstRoundId}\"[^>]*>(.*?)</select>",
            RegexOptions.Singleline);
        Assert.True(resultSelect.Success);
        Assert.Matches("<option[^>]+value=\"Extreme\"[^>]+selected(?:=\"selected\")?", resultSelect.Value);
        Assert.DoesNotMatch("<option[^>]+value=\"SpinFinish\"[^>]+selected(?:=\"selected\")?", resultSelect.Value);
    }

    [Fact]
    public async Task OldBattleNotificationAndSetupRoute_ResolveCompletedBattleToDetails()
    {
        using var firstClient = CreateClient();
        using var secondClient = CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var firstAccount = $"notification-first-{suffix}";
        var secondAccount = $"notification-second-{suffix}";
        const string password = "notification routing regression password";
        await RegisterAndLoginAsync(firstClient, firstAccount, password, $"通知甲-{suffix[..6]}");
        await RegisterAndLoginAsync(secondClient, secondAccount, password, $"通知乙-{suffix[..6]}");

        int battleId;
        int notificationId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var flow = scope.ServiceProvider.GetRequiredService<QuickBattleFlowService>();
            var first = await db.Users.SingleAsync(x => x.NormalizedAccount == firstAccount.ToUpperInvariant());
            var second = await db.Users.SingleAsync(x => x.NormalizedAccount == secondAccount.ToUpperInvariant());
            var invitation = (await flow.SendInvitationAsync(first.Id, second.Id)).Value!;
            battleId = (await flow.AcceptInvitationAsync(invitation.Id, second.Id)).Value;
            var battle = await db.Battles.SingleAsync(x => x.Id == battleId);
            battle.Status = BattleStatus.Completed;
            battle.CompletedAtUtc = DateTime.UtcNow;
            var notification = new UserNotification
            {
                UserId = first.Id,
                Kind = UserNotificationKind.InvitationAccepted,
                Title = "舊的陣容通知",
                Message = "請提交陣容",
                TargetUrl = $"/Battles/Setup/{battleId}",
                EntityType = "Battle",
                EntityId = battleId,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.UserNotifications.Add(notification);
            await db.SaveChangesAsync();
            notificationId = notification.Id;
        }

        using var notificationResponse = await firstClient.GetAsync($"/Notifications?handler=Open&id={notificationId}");
        Assert.Equal(HttpStatusCode.Redirect, notificationResponse.StatusCode);
        Assert.Equal($"/Battles/Details/{battleId}", notificationResponse.Headers.Location?.OriginalString);

        using var setupResponse = await firstClient.GetAsync($"/Battles/Setup/{battleId}");
        Assert.Equal(HttpStatusCode.Redirect, setupResponse.StatusCode);
        Assert.Equal($"/Battles/Details/{battleId}", setupResponse.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task TournamentPlayerSearch_ExcludesCurrentRegisteredAndPendingPlayers()
    {
        using var organizerClient = CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var organizerAccount = $"search-host-{suffix}";
        const string password = "tournament search regression password";
        await RegisterAndLoginAsync(organizerClient, organizerAccount, password, $"搜尋玩家-{suffix[..6]}-主辦");

        int tournamentId;
        int eligibleUserId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
            Assert.True((await auth.RegisterAsync($"search-registered-{suffix}", password, $"搜尋玩家-{suffix[..6]}-已報名")).Succeeded);
            Assert.True((await auth.RegisterAsync($"search-pending-{suffix}", password, $"搜尋玩家-{suffix[..6]}-已邀請")).Succeeded);
            Assert.True((await auth.RegisterAsync($"search-eligible-{suffix}", password, $"搜尋玩家-{suffix[..6]}-可邀請")).Succeeded);
            var organizer = await db.Users.SingleAsync(x => x.NormalizedAccount == organizerAccount.ToUpperInvariant());
            var registered = await db.Users.SingleAsync(x => x.NormalizedAccount == $"SEARCH-REGISTERED-{suffix}".ToUpperInvariant());
            var pending = await db.Users.SingleAsync(x => x.NormalizedAccount == $"SEARCH-PENDING-{suffix}".ToUpperInvariant());
            var eligible = await db.Users.SingleAsync(x => x.NormalizedAccount == $"SEARCH-ELIGIBLE-{suffix}".ToUpperInvariant());
            eligibleUserId = eligible.Id;
            var tournament = new Tournament
            {
                Name = $"搜尋測試-{suffix[..6]}",
                Mode = TournamentMode.Individual,
                Format = TournamentFormat.SingleElimination,
                RegistrationMode = TournamentRegistrationMode.Individual,
                RuleSet = TournamentRuleSet.IndividualThreeBladeFourPoints,
                BeybladesPerPlayer = 3,
                ScoreToWin = 4,
                TargetEntryCount = 4,
                OrganizerUserId = organizer.Id,
                RulesSnapshot = "test",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                Version = Guid.NewGuid().ToByteArray()
            };
            db.Tournaments.Add(tournament);
            await db.SaveChangesAsync();
            tournamentId = tournament.Id;
            db.TournamentEntries.Add(new TournamentEntry
            {
                TournamentId = tournament.Id,
                IndividualUserId = registered.Id,
                DisplayNameSnapshot = registered.DisplayName,
                Status = TournamentEntryStatus.Registered,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            db.TournamentInvitations.Add(new TournamentInvitation
            {
                TournamentId = tournament.Id,
                InvitedUserId = pending.Id,
                InvitedByUserId = organizer.Id,
                Type = TournamentInvitationType.Tournament,
                Status = TournamentInvitationStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var response = await organizerClient.GetAsync($"/Players/Search?q={suffix[..6]}&tournamentId={tournamentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var players = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var player = Assert.Single(players.EnumerateArray());
        Assert.Equal(eligibleUserId, player.GetProperty("userId").GetInt32());
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    private static async Task RegisterAndLoginAsync(HttpClient client, string account, string password, string displayName)
    {
        var registerToken = await GetAntiforgeryTokenAsync(client, "/Account/Register");
        using var registerResponse = await client.PostAsync("/Account/Register", Form(
            ("__RequestVerificationToken", registerToken),
            ("Account", account),
            ("Password", password),
            ("DisplayName", displayName)));
        Assert.Equal(HttpStatusCode.Redirect, registerResponse.StatusCode);

        var loginToken = await GetAntiforgeryTokenAsync(client, "/Account/Login");
        using var loginResponse = await client.PostAsync("/Account/Login", Form(
            ("__RequestVerificationToken", loginToken),
            ("Account", account),
            ("Password", password)));
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values) =>
        new(values.Select(value => new KeyValuePair<string, string>(value.Key, value.Value)));

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var tokenInput = Regex.Match(html, "<input(?=[^>]*name=\"__RequestVerificationToken\")[^>]*>", RegexOptions.IgnoreCase);
        var tokenValue = Regex.Match(tokenInput.Value, "value=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        Assert.True(tokenInput.Success && tokenValue.Success);
        return WebUtility.HtmlDecode(tokenValue.Groups[1].Value);
    }
}
