namespace BeybladeRecordSystem.Domain.Entities;

public class BeybladeConfigurationPart
{
    public int ConfigurationId { get; set; }
    public int PartId { get; set; }
    public string PartNameSnapshot { get; set; } = string.Empty;
    public BeybladeConfiguration Configuration { get; set; } = null!;
    public Part Part { get; set; } = null!;
}

