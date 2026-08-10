namespace BeybladeRecordSystem.Domain.Entities;

public class BattleLineup
{
    public int Id { get; set; }
    public int BattleId { get; set; }
    public int SequenceNo { get; set; }
    public int PositionNo { get; set; }
    public int PlayerABeybladeId { get; set; }
    public string PlayerABeybladeNameSnapshot { get; set; } = string.Empty;
    public int PlayerBBeybladeId { get; set; }
    public string PlayerBBeybladeNameSnapshot { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
    public Battle Battle { get; set; } = null!;
    public Beyblade PlayerABeyblade { get; set; } = null!;
    public Beyblade PlayerBBeyblade { get; set; } = null!;
}
