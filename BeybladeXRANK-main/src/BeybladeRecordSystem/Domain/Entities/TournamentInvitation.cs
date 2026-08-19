using BeybladeRecordSystem.Domain.Enums;

namespace BeybladeRecordSystem.Domain.Entities;

public class TournamentInvitation
{
    public int Id { get; set; }
    public int TournamentId { get; set; }
    public int? TournamentEntryId { get; set; }
    public int InvitedUserId { get; set; }
    public int InvitedByUserId { get; set; }
    public TournamentInvitationType Type { get; set; }
    public TournamentInvitationStatus Status { get; set; } = TournamentInvitationStatus.Pending;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? RespondedAtUtc { get; set; }
    public DateTime? InvalidatedAtUtc { get; set; }
    public Tournament Tournament { get; set; } = null!;
    public TournamentEntry? TournamentEntry { get; set; }
    public User InvitedUser { get; set; } = null!;
    public User InvitedByUser { get; set; } = null!;
}
