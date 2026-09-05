using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;

namespace BeybladeRecordSystem.Domain;

public static class BeybladeAssemblyRules
{
    public static string? Validate(IReadOnlyCollection<Part> parts)
    {
        if (parts.Count == 0) return "請選擇完整的零件配置。";
        if (parts.Any(x => !x.IsActive)) return "配置包含已停用的零件。";
        if (parts.Any(x => !Enum.IsDefined(x.Category) ||
            (x.IntegratesRatchet && x.Category is not (PartCategory.Blade or PartCategory.Bit))))
            return "零件的分類或一體式設定不正確。";
        if (parts.GroupBy(x => x.Category).Any(x => x.Count() > 1))
            return "同一配置的每個零件類別只能選擇一個。";

        var byCategory = parts.ToDictionary(x => x.Category);
        var upper = byCategory.Keys.Where(x => x is not (PartCategory.Ratchet or PartCategory.Bit)).ToHashSet();
        if (!upper.SetEquals([PartCategory.Blade]) &&
            !upper.SetEquals([PartCategory.LockChip, PartCategory.MainBlade, PartCategory.AssistBlade]) &&
            !upper.SetEquals([PartCategory.LockChip, PartCategory.OverBlade, PartCategory.MetalBlade, PartCategory.AssistBlade]))
            return "上蓋須為一般上蓋、CX 三件式或 CX 四件式完整結構，不能混用。";
        if (!byCategory.TryGetValue(PartCategory.Bit, out var bit))
            return "配置缺少軸心。";

        var bladeIntegratesRatchet = byCategory.TryGetValue(PartCategory.Blade, out var blade) && blade.IntegratesRatchet;
        var ratchetCount = (bladeIntegratesRatchet ? 1 : 0) + (bit.IntegratesRatchet ? 1 : 0) +
            (byCategory.ContainsKey(PartCategory.Ratchet) ? 1 : 0);
        return ratchetCount switch
        {
            0 => "配置缺少固鎖。",
            1 => null,
            _ => "固鎖位置重複：一體式零件不能再搭配固鎖或另一個固鎖一體式零件。"
        };
    }
}

