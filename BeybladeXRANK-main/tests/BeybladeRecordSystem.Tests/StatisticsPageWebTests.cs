using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BeybladeRecordSystem.Tests;

public sealed partial class StatisticsPageWebTests(AccountWebApplicationFactory factory)
    : IClassFixture<AccountWebApplicationFactory>
{
    [Fact]
    public async Task StatisticsPage_RendersResponsiveNavigationAndOnlyTheSelectedView()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        var suffix = Guid.NewGuid().ToString("N");
        var account = $"statistics-page-{suffix}";
        const string password = "statistics page regression password";
        await RegisterAndLoginAsync(client, account, password, $"戰績頁-{suffix[..6]}");

        using var overviewResponse = await client.GetAsync(
            "/Statistics?view=overview&personalSource=team&personalSide=b&personalSort=x-winrate-asc");

        Assert.Equal(HttpStatusCode.OK, overviewResponse.StatusCode);
        var overviewHtml = WebUtility.HtmlDecode(await overviewResponse.Content.ReadAsStringAsync());
        Assert.Contains("aria-label=\"戰績資訊分類\"", overviewHtml, StringComparison.Ordinal);
        Assert.Contains("id=\"statisticsView\"", overviewHtml, StringComparison.Ordinal);
        Assert.Contains("data-statistics-view=\"overview\"", overviewHtml, StringComparison.Ordinal);
        Assert.Contains("statistics-mobile-cards", overviewHtml, StringComparison.Ordinal);
        Assert.Contains("statistics-summary-card", overviewHtml, StringComparison.Ordinal);
        Assert.Matches(SelectedOption("overview"), overviewHtml);
        Assert.Matches(SelectedOption("team"), overviewHtml);
        Assert.Matches(SelectedOption("b"), overviewHtml);
        Assert.Matches(SelectedOption("x-winrate-asc"), overviewHtml);
        Assert.DoesNotContain("data-statistics-view=\"blades\"", overviewHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("data-statistics-view=\"opponents\"", overviewHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("data-statistics-view=\"history\"", overviewHtml, StringComparison.Ordinal);

        using var opponentResponse = await client.GetAsync("/Statistics?view=opponents&opponentSource=team");

        Assert.Equal(HttpStatusCode.OK, opponentResponse.StatusCode);
        var opponentHtml = WebUtility.HtmlDecode(await opponentResponse.Content.ReadAsStringAsync());
        Assert.Contains("data-statistics-view=\"opponents\"", opponentHtml, StringComparison.Ordinal);
        Assert.Contains("id=\"opponentSource\"", opponentHtml, StringComparison.Ordinal);
        Assert.Matches(SelectedOption("opponents"), opponentHtml);
        Assert.Matches(SelectedOption("team"), opponentHtml);
        Assert.DoesNotContain("data-statistics-view=\"overview\"", opponentHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatisticsPage_NormalizesUnknownViewToOverview()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        var suffix = Guid.NewGuid().ToString("N");
        var account = $"statistics-normalize-{suffix}";
        const string password = "statistics normalize regression password";
        await RegisterAndLoginAsync(client, account, password, $"戰績正規化-{suffix[..6]}");

        using var response = await client.GetAsync("/Statistics?view=unknown");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("data-statistics-view=\"overview\"", html, StringComparison.Ordinal);
        Assert.Matches(SelectedOption("overview"), html);
    }

    private static Regex SelectedOption(string value) => new(
        $"<option(?=[^>]*value=\"{Regex.Escape(value)}\")(?=[^>]*selected(?:=\"selected\")?)[^>]*>",
        RegexOptions.IgnoreCase);

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values) =>
        new(values.Select(value => new KeyValuePair<string, string>(value.Key, value.Value)));

    private static async Task RegisterAndLoginAsync(
        HttpClient client,
        string account,
        string password,
        string displayName)
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

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
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
