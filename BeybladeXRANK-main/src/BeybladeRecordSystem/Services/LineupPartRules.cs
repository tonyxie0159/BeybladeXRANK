using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Services;

internal static class LineupPartRules
{
    private const int AdvisoryLockNamespace = 20260905;

    public static async Task AcquireSubmissionLockAsync(AppDbContext db, int ownerId)
    {
        if (db.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({AdvisoryLockNamespace}, {ownerId})");
    }

    public static ServiceResult ValidateNoDuplicates(IEnumerable<BeybladeConfiguration?> configurations)
    {
        var duplicates = configurations
            .Where(x => x is not null)
            .SelectMany(x => x!.Parts)
            .GroupBy(x => x.PartId)
            .Where(x => x.Count() > 1)
            .Select(x => x.First().PartNameSnapshot)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return duplicates.Length == 0
            ? ServiceResult.Success()
            : ServiceResult.Failure($"同一隊伍不可重複使用零件：{string.Join("、", duplicates)}。");
    }
}
