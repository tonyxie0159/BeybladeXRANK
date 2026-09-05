namespace BeybladeRecordSystem.Domain.Entities;

public class Beyblade
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public User User { get; set; } = null!;
    public string? UpperName { get; set; }
    public List<BeybladeConfiguration> Configurations { get; set; } = [];
    // Convenience view only; battle submissions must choose an explicit configuration.
    public BeybladeConfiguration? Configuration
    {
        get => Configurations.OrderByDescending(x => x.VersionNo).FirstOrDefault();
        set { Configurations = value is null ? [] : [value]; }
    }
    public string DisplayName => Configuration is { Parts.Count: > 0 }
        ? $"{Name} · {Configuration.CommonName}" : Name;
}
