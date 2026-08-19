namespace BeybladeRecordSystem.Domain.Entities;

public class BattleLineup
{
    public int Id { get; set; }
    public int BattleId { get; set; }
    public int SequenceNo { get; set; }
    public int PositionNo { get; set; }
    public int? PlayerAId { get; set; }
    public string PlayerADisplayNameSnapshot { get; set; } = string.Empty;
    public int PlayerABeybladeId { get; set; }
    public string PlayerABeybladeNameSnapshot { get; set; } = string.Empty;
    public int? PlayerBId { get; set; }
    public string PlayerBDisplayNameSnapshot { get; set; } = string.Empty;
    public int PlayerBBeybladeId { get; set; }
    public string PlayerBBeybladeNameSnapshot { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
    public Battle Battle { get; set; } = null!;
    public User? PlayerA { get; set; }
    public Beyblade PlayerABeyblade { get; set; } = null!;
    public User? PlayerB { get; set; }
    public Beyblade PlayerBBeyblade { get; set; } = null!;
}
