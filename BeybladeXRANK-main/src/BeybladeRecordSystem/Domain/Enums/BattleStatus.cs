namespace BeybladeRecordSystem.Domain.Enums;

public enum BattleStatus
{
    // Keep the original numeric values stable for existing persisted battles.
    Draft = 0,
    LineupLocked = 1,
    InProgress = 2,
    VictoryPendingCompletion = 3,
    Completed = 4,
    Forfeited = 5,
    Cancelled = 6,
    Voided = 7,
    LineupSelection = 8,
    LineupReview = 9,
    SideSelection = 10,
    ReorderSelection = 11
}
