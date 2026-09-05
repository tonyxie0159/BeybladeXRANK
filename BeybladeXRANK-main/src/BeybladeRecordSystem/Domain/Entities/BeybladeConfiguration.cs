namespace BeybladeRecordSystem.Domain.Entities;

public class BeybladeConfiguration
{
    public int Id { get; set; }
    public int BeybladeId { get; set; }
    public int VersionNo { get; set; } = 1;
    public string PartsKey { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public Beyblade Beyblade { get; set; } = null!;
    public List<BeybladeConfigurationPart> Parts { get; set; } = [];
    public string CommonName => BeybladeNaming.CommonName(Parts);
    public string UpperName => BeybladeNaming.UpperName(Parts);
    public string VersionLabel => $"v{VersionNo} · {CommonName}";
    public string PartsSummary => string.Join(" / ", Parts.OrderBy(x => x.Part.Category).Select(x => x.PartNameSnapshot));
}
