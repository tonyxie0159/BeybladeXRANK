namespace BeybladeRecordSystem.Domain.Entities;

public class BattleTeamOrderSelection
{
    public int Id { get; set; }
    public int BattleId { get; set; }
    public int SequenceNo { get; set; } = 1;
    public int TournamentEntryId { get; set; }
    public int UserId { get; set; }
    public int PositionNo { get; set; }
    public int SubmittedByUserId { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public Battle Battle { get; set; } = null!;
    public TournamentEntry TournamentEntry { get; set; } = null!;
    public User User { get; set; } = null!;
    public User SubmittedByUser { get; set; } = null!;
}
