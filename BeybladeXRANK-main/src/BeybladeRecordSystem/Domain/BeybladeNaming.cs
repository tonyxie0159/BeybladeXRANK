using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;

namespace BeybladeRecordSystem.Domain;

public static class BeybladeNaming
{
    // Read snapshots, so a later catalog spelling correction does not rename a configuration.
    public static string CommonName(IEnumerable<BeybladeConfigurationPart> parts)
    {
        var list = parts.ToList();
        return UpperName(list) + string.Concat(list.Where(x => x.Part.Category is PartCategory.Ratchet or PartCategory.Bit)
            .OrderBy(x => x.Part.Category).Select(x => x.PartNameSnapshot));
    }

    public static string UpperName(IEnumerable<BeybladeConfigurationPart> parts)
    {
        var names = parts.ToDictionary(x => x.Part.Category, x => x.PartNameSnapshot);
        string Name(PartCategory category) => names.GetValueOrDefault(category, string.Empty);
        var upper = names.ContainsKey(PartCategory.Blade)
            ? Name(PartCategory.Blade)
            : Name(PartCategory.LockChip) + (names.ContainsKey(PartCategory.MainBlade)
                ? Name(PartCategory.MainBlade) : Name(PartCategory.MetalBlade));
        return upper;
    }
}
