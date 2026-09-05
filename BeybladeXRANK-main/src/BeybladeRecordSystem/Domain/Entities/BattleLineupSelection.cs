namespace BeybladeRecordSystem.Domain.Entities;

public class BattleLineupSelection
{
    public int Id { get; set; }
    public int BattleId { get; set; }
    public int SequenceNo { get; set; } = 1;
    public int UserId { get; set; }
    public int PositionNo { get; set; }
    public int BeybladeId { get; set; }
    public int? BeybladeConfigurationId { get; set; }
    public BeybladeConfiguration? BeybladeConfiguration { get; set; }
    public string PlayerDisplayNameSnapshot { get; set; } = string.Empty;
    public string BeybladeNameSnapshot { get; set; } = string.Empty;
    public DateTime SubmittedAtUtc { get; set; }
    public Battle Battle { get; set; } = null!;
    public User User { get; set; } = null!;
    public Beyblade Beyblade { get; set; } = null!;
}
