using BeybladeRecordSystem.Domain.Enums;

namespace BeybladeRecordSystem.Domain.Entities;

public class BattleRoundEvent
{
    public int Id { get; set; }
    public int BattleRoundId { get; set; }
    public int EventSequence { get; set; }
    public BattleRoundEventType EventType { get; set; }
    public int? ActorPlayerId { get; set; }
    public int? WinnerPlayerId { get; set; }
    public ResultType? ResultType { get; set; }
    public int ScoreAwarded { get; set; }
    public bool IsEffective { get; set; } = true;
    public BattleRoundEventInvalidationReason? InvalidationReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public BattleRound BattleRound { get; set; } = null!;
}
