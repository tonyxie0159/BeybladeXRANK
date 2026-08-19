using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;

namespace BeybladeRecordSystem.Domain;

public static class BattleRules
{
    public static int ScoreFor(ResultType resultType) => resultType switch
    {
        ResultType.SpinFinish => 1,
        ResultType.KnockOut => 2,
        ResultType.Burst => 2,
        ResultType.Extreme => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(resultType))
    };

    public static BattleStatus StatusForScore(int sideAScore, int sideBScore, int scoreToWin = 4) =>
        sideAScore >= scoreToWin || sideBScore >= scoreToWin
            ? BattleStatus.VictoryPendingCompletion
            : BattleStatus.InProgress;

    public static BattleSide Opposite(BattleSide side) => side == BattleSide.B ? BattleSide.X : BattleSide.B;

    public static int FaultCount(IEnumerable<BattleRoundEvent> events, int actorPlayerId)
    {
        var count = 0;
        foreach (var battleEvent in events.Where(x => x.IsEffective).OrderBy(x => x.EventSequence))
        {
            if (battleEvent.EventType == BattleRoundEventType.LaunchFault && battleEvent.ActorPlayerId == actorPlayerId)
                count++;
            if (battleEvent.EventType == BattleRoundEventType.LaunchFaultPenalty && battleEvent.ActorPlayerId == actorPlayerId)
                count = 0;
        }
        return count;
    }
}
