using BeybladeRecordSystem.Domain.Entities;

namespace BeybladeRecordSystem.Services;

internal static class LineupVersions
{
    public static ServiceResult<Dictionary<int, BeybladeConfiguration?>> Resolve(
        IReadOnlyList<int> bladeIds, IReadOnlyList<int>? configurationIds, IReadOnlyDictionary<int, Beyblade> blades)
    {
        if (configurationIds is not null && configurationIds.Count != bladeIds.Count)
            return ServiceResult<Dictionary<int, BeybladeConfiguration?>>.Failure("每個陀螺位置都必須選擇版本。");
        var result = new Dictionary<int, BeybladeConfiguration?>();
        for (var index = 0; index < bladeIds.Count; index++)
        {
            var blade = blades[bladeIds[index]];
            var versions = blade.Configurations;
            if (configurationIds is null)
            {
                // Compatibility for existing service callers: never guess among multiple versions.
                if (versions.Count > 1)
                    return ServiceResult<Dictionary<int, BeybladeConfiguration?>>.Failure("此陀螺有多個版本，請明確選擇出戰版本。");
                result[blade.Id] = versions.SingleOrDefault();
            }
            else if (configurationIds[index] == 0 && versions.Count == 0)
                result[blade.Id] = null; // Preserve the existing legacy-battle flow.
            else
            {
                var version = versions.SingleOrDefault(x => x.Id == configurationIds[index]);
                if (version is null)
                    return ServiceResult<Dictionary<int, BeybladeConfiguration?>>.Failure("所選版本不屬於該陀螺，請重新選擇。");
                result[blade.Id] = version;
            }
        }
        return ServiceResult<Dictionary<int, BeybladeConfiguration?>>.Success(result);
    }

    public static bool Matches(IReadOnlyList<BattleLineupSelection> existing, IReadOnlyList<int> bladeIds, IReadOnlyList<int>? configurationIds) =>
        existing.Select(x => x.BeybladeId).SequenceEqual(bladeIds) &&
        (configurationIds is null || existing.Select(x => x.BeybladeConfigurationId ?? 0).SequenceEqual(configurationIds));

    public static string Snapshot(Beyblade blade, BeybladeConfiguration? configuration) =>
        configuration is null ? blade.Name : $"{blade.Name} · {configuration.VersionLabel}";
}
