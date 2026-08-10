using BeybladeRecordSystem.Domain.Enums;

namespace BeybladeRecordSystem.Domain.Entities;

public class BattleRound
{
    public int Id { get; set; }
    public int BattleId { get; set; }
    public int LineupId { get; set; }
    public int RoundNo { get; set; }
    public int PositionNo { get; set; }
    public int PlayerABeybladeId { get; set; }
    public string PlayerABeybladeNameSnapshot { get; set; } = string.Empty;
    public int PlayerBBeybladeId { get; set; }
    public string PlayerBBeybladeNameSnapshot { get; set; } = string.Empty;
    public BattleRoundStatus Status { get; set; } = BattleRoundStatus.InProgress;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public Battle Battle { get; set; } = null!;
    public BattleLineup Lineup { get; set; } = null!;
    public ICollection<BattleRoundEvent> Events { get; set; } = new List<BattleRoundEvent>();
    public ICollection<BattleRoundRevision> Revisions { get; set; } = new List<BattleRoundRevision>();
}
