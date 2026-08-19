namespace BeybladeRecordSystem.Domain.Entities;

public class TournamentEntryMember
{
    public int Id { get; set; }
    public int TournamentId { get; set; }
    public int TournamentEntryId { get; set; }
    public int UserId { get; set; }
    public int MemberOrder { get; set; }
    public bool IsRepresentative { get; set; }
    public string DisplayNameSnapshot { get; set; } = string.Empty;
    public DateTime JoinedAtUtc { get; set; }
    public Tournament Tournament { get; set; } = null!;
    public TournamentEntry TournamentEntry { get; set; } = null!;
    public User User { get; set; } = null!;
}
