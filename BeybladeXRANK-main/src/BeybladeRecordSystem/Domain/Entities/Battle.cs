using BeybladeRecordSystem.Domain.Enums;
using System.ComponentModel.DataAnnotations.Schema;

namespace BeybladeRecordSystem.Domain.Entities;

public class Battle
{
    public int Id { get; set; }
    public BattleSourceType SourceType { get; set; } = BattleSourceType.Quick;
    public int ScoreToWin { get; set; } = 4;
    public int? TournamentMatchId { get; set; }
    public int? PlayerAId { get; set; }
    public int? PlayerBId { get; set; }
    public int CreatedByUserId { get; set; }
    public BattleStatus Status { get; set; } = BattleStatus.Draft;
    public int SideAScore { get; set; }
    public int SideBScore { get; set; }
    public BattleSide? SideADesignation { get; set; }
    public BattleSide? WinningSide { get; set; }
    public int? WinningPlayerId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public byte[] Version { get; set; } = Array.Empty<byte>();
    public User PlayerA { get; set; } = null!;
    public User PlayerB { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
    public TournamentMatch? TournamentMatch { get; set; }
    public ICollection<BattleLineup> Lineups { get; set; } = new List<BattleLineup>();
    public ICollection<BattleLineupSelection> LineupSelections { get; set; } = new List<BattleLineupSelection>();
    public ICollection<BattleTeamOrderSelection> TeamOrderSelections { get; set; } = new List<BattleTeamOrderSelection>();
    public ICollection<BattleRound> Rounds { get; set; } = new List<BattleRound>();

    // Compatibility aliases for the existing quick-battle UI and statistics queries.
    [NotMapped]
    public int PlayerAScore
    {
        get => SideAScore;
        set => SideAScore = value;
    }

    [NotMapped]
    public int PlayerBScore
    {
        get => SideBScore;
        set => SideBScore = value;
    }
}
