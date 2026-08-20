namespace BeybladeRecordSystem.Domain.Entities;

public class BattleRoundRevision
{
    public int Id { get; set; }
    public int BattleRoundId { get; set; }
    public int ChangedByUserId { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public string? Reason { get; set; }
    public string PreviousEffectiveEventSnapshot { get; set; } = string.Empty;
    public string NewEffectiveEventSnapshot { get; set; } = string.Empty;
    public string PreviousBattleSnapshot { get; set; } = string.Empty;
    public string NewBattleSnapshot { get; set; } = string.Empty;
    public BattleRound BattleRound { get; set; } = null!;
    public User ChangedByUser { get; set; } = null!;
}
