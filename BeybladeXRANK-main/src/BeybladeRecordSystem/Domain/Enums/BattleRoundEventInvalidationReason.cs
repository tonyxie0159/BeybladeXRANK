namespace BeybladeRecordSystem.Domain.Enums;

public enum BattleRoundEventInvalidationReason
{
    SupersededByRevision,
    SupersededByEarlierRoundRevision,
    VictoryThresholdReached,
    BattleTerminated
}
