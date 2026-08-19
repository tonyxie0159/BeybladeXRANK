using BeybladeRecordSystem.Domain.Enums;

namespace BeybladeRecordSystem.Domain.Entities;

public class TournamentEntry
{
    public int Id { get; set; }
    public int TournamentId { get; set; }
    public string? RegistrationNumber { get; set; }
    public int? SchedulePosition { get; set; }
    public string DisplayNameSnapshot { get; set; } = string.Empty;
    public string? TeamName { get; set; }
    public int? IndividualUserId { get; set; }
    public TournamentEntryStatus Status { get; set; } = TournamentEntryStatus.Pending;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? RegisteredAtUtc { get; set; }
    public DateTime? WithdrawnAtUtc { get; set; }
    public Tournament Tournament { get; set; } = null!;
    public User? IndividualUser { get; set; }
    public ICollection<TournamentEntryMember> Members { get; set; } = new List<TournamentEntryMember>();
}
