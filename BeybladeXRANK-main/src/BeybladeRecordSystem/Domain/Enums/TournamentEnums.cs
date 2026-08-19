namespace BeybladeRecordSystem.Domain.Enums;

public enum TournamentMode
{
    Individual,
    Team
}

public enum TournamentRegistrationMode
{
    Individual,
    CompleteTeam,
    SystemAssignedTeam
}

public enum TournamentRuleSet
{
    IndividualThreeBladeFourPoints,
    DuoSixBladeEightPoints,
    DuoFourBladeSixPoints,
    TrioThreeBladeFourPoints,
    TrioThreeBladeFivePoints
}

public enum TournamentStatus
{
    RegistrationOpen,
    InProgress,
    Completed,
    Cancelled
}

public enum TournamentRegistrationStage
{
    Open,
    CapacityReached,
    Closed,
    AwaitingTeamFormation,
    ScheduleDraftCreated,
    AwaitingStart
}

public enum TournamentEntryStatus
{
    Pending,
    Registered,
    Withdrawn,
    Forfeited
}

public enum TournamentInvitationType
{
    Tournament,
    Team,
    RepresentativeTransfer
}

public enum TournamentInvitationStatus
{
    Pending,
    Accepted,
    Declined,
    Invalidated,
    Cancelled
}

public enum TournamentParticipationStatus
{
    Pending,
    Accepted,
    Declined,
    Invalidated
}

public enum TournamentMatchStatus
{
    WaitingForParticipants,
    AwaitingParticipationConfirmation,
    ReadyForLineup,
    LineupSelection,
    TeamOrderSelection,
    LineupReview,
    LineupLocked,
    SideSelection,
    InProgress,
    ReorderSelection,
    VictoryPendingCompletion,
    Completed,
    Forfeited,
    Walkover,
    Voided,
    NotRequired
}
