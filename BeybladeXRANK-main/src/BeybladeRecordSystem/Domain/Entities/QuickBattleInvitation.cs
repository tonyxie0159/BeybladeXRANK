namespace BeybladeRecordSystem.Domain.Entities;

public class QuickBattleInvitation
{
    public int Id { get; set; }
    public int InviterUserId { get; set; }
    public int InviteeUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public byte[] Version { get; set; } = Array.Empty<byte>();
    public User InviterUser { get; set; } = null!;
    public User InviteeUser { get; set; } = null!;
}
