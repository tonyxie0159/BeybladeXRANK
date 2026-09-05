using BeybladeRecordSystem.Domain.Enums;

namespace BeybladeRecordSystem.ViewModels;

public sealed record BeybladePerformanceInsight(string Tone, string Text);
public sealed record BeybladeVersionStatisticsRow(
    int? ConfigurationId,
    string Label,
    string Parts,
    BeybladeStatisticsViewModel Summary,
    IReadOnlyList<BeybladePerformanceInsight> Insights);
public sealed record BeybladeVersionStatistics(string Name, string? UpperName, BeybladeStatisticsViewModel Total, IReadOnlyList<BeybladeVersionStatisticsRow> Versions);

public sealed record SideStatisticsViewModel(int Wins, int Losses, decimal WinRate)
{
    public int Samples => Wins + Losses;
}
public sealed record UserSummaryViewModel(
    int Wins,
    int Losses,
    decimal WinRate,
    int Score,
    int AgainstScore,
    int LaunchFaultAgainstScore,
    SideStatisticsViewModel BSide,
    SideStatisticsViewModel XSide)
{
    public int ScoreDifference => Score - AgainstScore;
}
public sealed record UserStatisticsSectionsViewModel(
    UserSummaryViewModel Quick,
    UserSummaryViewModel TournamentIndividual,
    UserSummaryViewModel TournamentTeamResult,
    UserSummaryViewModel TournamentTeamRoundPerformance);
public sealed record UserStatisticsRowViewModel(string Key, string Label, UserSummaryViewModel Summary);
public sealed record ResultTypeStatisticsViewModel(
    int SpinFinishFor, int KnockOutFor, int BurstFor, int ExtremeFor,
    int SpinFinishAgainst, int KnockOutAgainst, int BurstAgainst, int ExtremeAgainst);
public sealed record BeybladeStatisticsViewModel(
    int BeybladeId,
    string Name,
    int Wins,
    int Losses,
    decimal WinRate,
    int Score,
    int AgainstScore,
    int LaunchFaultAgainstScore,
    int RoundCount,
    decimal AverageScore,
    decimal AverageAgainstScore,
    int LaunchFaultCount,
    ResultTypeStatisticsViewModel ResultTypes,
    SideStatisticsViewModel BSide,
    SideStatisticsViewModel XSide)
{
    public int ScoreDifference => Score - AgainstScore;
}
public sealed record BeybladeSourceSamplesViewModel(int All, int Quick, int TournamentIndividual, int TournamentTeam);
public sealed record StatisticsSideSamplesViewModel(int All, int B, int X, int Unassigned);
public sealed record OpponentStatisticsViewModel(int OpponentId, string DisplayName, int Wins, int Losses, decimal WinRate, int Score, int AgainstScore);
public sealed record OpponentBeybladeStatisticsViewModel(string MyBeybladeName, string OpponentBeybladeName, int Wins, int Losses, decimal WinRate, int Score, int AgainstScore);
public sealed record BattleHistoryViewModel(
    int BattleId,
    string OpponentDisplayName,
    int MyScore,
    int OpponentScore,
    bool Won,
    DateTime? CompletedAtUtc,
    BattleSourceType SourceType,
    bool IsTeamResult,
    BattleSide? Side);
