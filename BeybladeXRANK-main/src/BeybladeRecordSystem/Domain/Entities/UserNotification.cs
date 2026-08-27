using BeybladeRecordSystem.Domain.Enums;

namespace BeybladeRecordSystem.Domain.Entities;

public class UserNotification
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public UserNotificationKind Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = "/Notifications";
    public string? EntityType { get; set; }
    public int? EntityId { get; set; }
    public UserNotificationActionType ActionType { get; set; }
    public int? ActionEntityId { get; set; }
    public string? DedupeKey { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public User User { get; set; } = null!;
}
