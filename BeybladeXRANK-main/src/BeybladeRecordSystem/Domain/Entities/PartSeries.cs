using BeybladeRecordSystem.Domain.Enums;

namespace BeybladeRecordSystem.Domain.Entities;

public class PartSeries
{
    public int PartId { get; set; }
    public PartSystemSeries Series { get; set; }
    public Part Part { get; set; } = null!;
}

