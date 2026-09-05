using BeybladeRecordSystem.Domain.Enums;

namespace BeybladeRecordSystem.Domain.Entities;

public class Part
{
    public int Id { get; set; }
    public PartCategory Category { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IntegratesRatchet { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<PartSeries> Series { get; set; } = [];
}

