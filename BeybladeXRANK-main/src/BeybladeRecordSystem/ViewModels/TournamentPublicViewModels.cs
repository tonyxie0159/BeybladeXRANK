using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Domain.Tournaments;

namespace BeybladeRecordSystem.ViewModels;

public sealed record TournamentPublicEntryViewModel(
    int Id,
    string? RegistrationNumber,
    int? SchedulePosition,
    string DisplayName,
    IReadOnlyList<string> MemberDisplayNames);

public sealed record TournamentPublicLineupPositionViewModel(
    int SequenceNo,
    int PositionNo,
    string PlayerADisplayName,
    string PlayerABeybladeName,
    string PlayerBDisplayName,
    string PlayerBBeybladeName,
    bool IsCurrent);

public sealed record TournamentPublicBattleViewModel(
    int Id,
    BattleStatus Status,
    int ScoreToWin,
    int SideAScore,
    int SideBScore,
    BattleSide? SideADesignation,
    IReadOnlyList<TournamentPublicLineupPositionViewModel> Lineup);

public sealed record TournamentPublicMatchViewModel(
    int Id,
    TournamentBracket Bracket,
    int RoundNumber,
    int MatchNumber,
    int SequenceNumber,
    TournamentMatchStatus Status,
    string SideALabel,
    string SideBLabel,
    string? WinnerLabel,
    string? LoserLabel,
    bool IsBye,
    bool IsSeedQualifier,
    bool IsResetFinal,
    bool IsCurrent,
    bool CanOpenWorkspace,
    string? ResolutionSummary,
    DateTime? CompletedAtUtc,
    TournamentPublicBattleViewModel? Battle);

public sealed record TournamentPublicDetailsViewModel(
    int Id,
    string Name,
    string OrganizerDisplayName,
    TournamentMode Mode,
    TournamentRegistrationMode RegistrationMode,
    TournamentFormat Format,
    TournamentRuleSet RuleSet,
    TournamentStatus Status,
    TournamentRegistrationStage RegistrationStage,
    int? TeamSize,
    int BeybladesPerPlayer,
    int ScoreToWin,
    int TargetEntryCount,
    string RulesSnapshot,
    string? Notes,
    string? CancellationReason,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? RegistrationClosedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? CancelledAtUtc,
    IReadOnlyList<TournamentPublicEntryViewModel> Entries,
    IReadOnlyList<TournamentPublicMatchViewModel> Matches,
    string PollToken);
