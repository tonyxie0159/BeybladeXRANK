using BeybladeRecordSystem.Domain.Enums;

namespace BeybladeRecordSystem.Domain.Entities;

public class TournamentMatchParticipant
{
    public int Id { get; set; }
    public int TournamentMatchId { get; set; }
    public int TournamentEntryId { get; set; }
    public int UserId { get; set; }
    public TournamentParticipationStatus Status { get; set; } = TournamentParticipationStatus.Pending;
    public bool IsMatchRepresentative { get; set; }
    public bool LineupConfirmed { get; set; }
    public DateTime NotifiedAtUtc { get; set; }
    public DateTime? RespondedAtUtc { get; set; }
    public DateTime? LineupConfirmedAtUtc { get; set; }
    public byte[] Version { get; set; } = Array.Empty<byte>();
    public TournamentMatch TournamentMatch { get; set; } = null!;
    public TournamentEntry TournamentEntry { get; set; } = null!;
    public User User { get; set; } = null!;
}
