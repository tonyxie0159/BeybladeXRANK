using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Services;

public sealed record LineupPresetItem(int BeybladeId, int ConfigurationId);

internal static class LineupPreset
{
    public static async Task<IReadOnlyList<LineupPresetItem>> GetMostRecentValidAsync(
        AppDbContext db,
        int userId,
        int requiredCount,
        IReadOnlyList<Beyblade> availableBeyblades)
    {
        if (requiredCount < 1 || availableBeyblades.Count < requiredCount) return [];

        var recentBattleIds = await db.Battles.AsNoTracking()
            .Where(x =>
                (x.Status == BattleStatus.Completed || x.Status == BattleStatus.Forfeited) &&
                x.LineupSelections.Any(selection => selection.UserId == userId))
            .OrderByDescending(x => x.CompletedAtUtc)
            .ThenByDescending(x => x.Id)
            .Select(x => x.Id)
            .Take(10)
            .ToListAsync();
        if (recentBattleIds.Count == 0) return [];

        var selections = await db.BattleLineupSelections.AsNoTracking()
            .Where(x => recentBattleIds.Contains(x.BattleId) && x.UserId == userId)
            .OrderBy(x => x.SequenceNo)
            .ThenBy(x => x.PositionNo)
            .ToListAsync();
        var available = availableBeyblades.ToDictionary(x => x.Id);

        foreach (var battleId in recentBattleIds)
        {
            var battleSelections = selections.Where(x => x.BattleId == battleId).ToList();
            if (battleSelections.Count == 0) continue;
            var latestSequence = battleSelections.Max(x => x.SequenceNo);
            var latest = battleSelections.Where(x => x.SequenceNo == latestSequence)
                .OrderBy(x => x.PositionNo)
                .ToList();
            if (latest.Count != requiredCount ||
                !latest.Select(x => x.PositionNo).SequenceEqual(Enumerable.Range(1, requiredCount)) ||
                latest.Select(x => x.BeybladeId).Distinct().Count() != requiredCount)
                continue;

            var preset = new List<LineupPresetItem>(requiredCount);
            var valid = true;
            foreach (var selection in latest)
            {
                if (selection.BeybladeConfigurationId is not int configurationId ||
                    !available.TryGetValue(selection.BeybladeId, out var blade) ||
                    blade.Configurations.All(x => x.Id != configurationId))
                {
                    valid = false;
                    break;
                }
                preset.Add(new LineupPresetItem(selection.BeybladeId, configurationId));
            }
            if (valid) return preset;
        }

        return [];
    }
}
