using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Domain.Tournaments;

namespace BeybladeRecordSystem.Domain.Entities;

public class TournamentMatch
{
    public int Id { get; set; }
    public int TournamentId { get; set; }
    public TournamentBracket Bracket { get; set; }
    public int RoundNumber { get; set; }
    public int MatchNumber { get; set; }
    public int SequenceNumber { get; set; }
    public TournamentMatchStatus Status { get; set; } = TournamentMatchStatus.WaitingForParticipants;
    public TournamentParticipantSourceKind SideASourceKind { get; set; }
    public int SideASourceReferenceId { get; set; }
    public TournamentParticipantSourceKind? SideBSourceKind { get; set; }
    public int? SideBSourceReferenceId { get; set; }
    public int? SideAEntryId { get; set; }
    public int? SideBEntryId { get; set; }
    public int? WinnerEntryId { get; set; }
    public int? LoserEntryId { get; set; }
    public int? WinnerToMatchId { get; set; }
    public int? LoserToMatchId { get; set; }
    public bool IsBye { get; set; }
    public bool IsSeedQualifier { get; set; }
    public bool IsResetFinal { get; set; }
    public string? ResolutionReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public byte[] Version { get; set; } = Array.Empty<byte>();
    public Tournament Tournament { get; set; } = null!;
    public TournamentEntry? SideAEntry { get; set; }
    public TournamentEntry? SideBEntry { get; set; }
    public TournamentEntry? WinnerEntry { get; set; }
    public TournamentEntry? LoserEntry { get; set; }
    public TournamentMatch? WinnerToMatch { get; set; }
    public TournamentMatch? LoserToMatch { get; set; }
    public Battle? Battle { get; set; }
    public ICollection<Battle> VoidedBattles { get; set; } = new List<Battle>();
    public ICollection<TournamentMatchParticipant> Participants { get; set; } = new List<TournamentMatchParticipant>();
}
