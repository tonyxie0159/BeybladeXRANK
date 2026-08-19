using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Domain.Tournaments;

namespace BeybladeRecordSystem.Domain.Entities;

public class Tournament
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public TournamentMode Mode { get; set; }
    public TournamentFormat Format { get; set; }
    public TournamentRegistrationMode RegistrationMode { get; set; }
    public TournamentRuleSet RuleSet { get; set; }
    public TournamentStatus Status { get; set; } = TournamentStatus.RegistrationOpen;
    public TournamentRegistrationStage RegistrationStage { get; set; } = TournamentRegistrationStage.Open;
    public int? TeamSize { get; set; }
    public int BeybladesPerPlayer { get; set; }
    public int ScoreToWin { get; set; }
    public int TargetEntryCount { get; set; }
    public int OrganizerUserId { get; set; }
    public string? Notes { get; set; }
    public string RulesSnapshot { get; set; } = string.Empty;
    public string? CancellationReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? RegistrationClosedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public byte[] Version { get; set; } = Array.Empty<byte>();
    public User OrganizerUser { get; set; } = null!;
    public ICollection<TournamentEntry> Entries { get; set; } = new List<TournamentEntry>();
    public ICollection<TournamentInvitation> Invitations { get; set; } = new List<TournamentInvitation>();
    public ICollection<TournamentMatch> Matches { get; set; } = new List<TournamentMatch>();
}
