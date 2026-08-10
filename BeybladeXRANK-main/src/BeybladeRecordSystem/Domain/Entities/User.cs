namespace BeybladeRecordSystem.Domain.Entities;

public class User
{
    public int Id { get; set; }
    public string Account { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public ICollection<Beyblade> Beyblades { get; set; } = new List<Beyblade>();
}
