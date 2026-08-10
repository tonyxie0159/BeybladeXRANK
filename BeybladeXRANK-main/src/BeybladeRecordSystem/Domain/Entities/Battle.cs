using BeybladeRecordSystem.Domain.Enums;

namespace BeybladeRecordSystem.Domain.Entities;

public class Battle
{
    public int Id { get; set; }
    public int PlayerAId { get; set; }
    public int PlayerBId { get; set; }
    public int CreatedByUserId { get; set; }
    public BattleStatus Status { get; set; } = BattleStatus.Draft;
    public int PlayerAScore { get; set; }
    public int PlayerBScore { get; set; }
    public int? WinningPlayerId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public byte[] Version { get; set; } = Array.Empty<byte>();
    public User PlayerA { get; set; } = null!;
    public User PlayerB { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
    public ICollection<BattleLineup> Lineups { get; set; } = new List<BattleLineup>();
    public ICollection<BattleRound> Rounds { get; set; } = new List<BattleRound>();
}
