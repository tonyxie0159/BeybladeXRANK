namespace BeybladeRecordSystem.ViewModels;

public sealed record UserSummaryViewModel(int Wins, int Losses, decimal WinRate, int Score, int AgainstScore, int LaunchFaultAgainstScore);
public sealed record BeybladeStatisticsViewModel(int BeybladeId, string Name, int Wins, int Losses, decimal WinRate, int Score, int AgainstScore, int LaunchFaultAgainstScore);
public sealed record OpponentStatisticsViewModel(int OpponentId, string DisplayName, int Wins, int Losses, decimal WinRate, int Score, int AgainstScore);
public sealed record OpponentBeybladeStatisticsViewModel(string MyBeybladeName, string OpponentBeybladeName, int Wins, int Losses, decimal WinRate, int Score, int AgainstScore);
public sealed record BattleHistoryViewModel(int BattleId, string OpponentDisplayName, int MyScore, int OpponentScore, bool Won, DateTime? CompletedAtUtc);
