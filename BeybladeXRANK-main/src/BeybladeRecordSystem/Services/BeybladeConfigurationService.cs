using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Services;

public class BeybladeConfigurationService(AppDbContext db)
{
    public Task<List<Part>> GetActivePartsAsync(PartSystemSeries? series = null) =>
        db.Parts.AsNoTracking().Where(x => x.IsActive && (series == null || x.Series.Any(s => s.Series == series)))
            .OrderBy(x => x.Category).ThenBy(x => x.Name).ToListAsync();

    public Task<BeybladeConfiguration?> GetMineAsync(int userId, int beybladeId) =>
        db.BeybladeConfigurations.AsNoTracking().Include(x => x.Parts).ThenInclude(x => x.Part)
            .Where(x => x.BeybladeId == beybladeId && x.Beyblade.UserId == userId && !x.Beyblade.IsDeleted)
            .OrderByDescending(x => x.VersionNo).FirstOrDefaultAsync();

    public Task<List<BeybladeConfiguration>> GetVersionsAsync(int userId, int beybladeId) =>
        db.BeybladeConfigurations.AsNoTracking().Include(x => x.Parts).ThenInclude(x => x.Part)
            .Where(x => x.BeybladeId == beybladeId && x.Beyblade.UserId == userId && !x.Beyblade.IsDeleted)
            .OrderByDescending(x => x.VersionNo).ToListAsync();

    public async Task<ServiceResult> RecordAsync(int userId, int beybladeId, IReadOnlyList<int> partIds, string? customName = null)
    {
        if (partIds.Count is < 2 or > 6 || partIds.Any(x => x <= 0) || partIds.Distinct().Count() != partIds.Count)
            return ServiceResult.Failure("請選擇完整且不重複的零件。");

        await using var transaction = await db.Database.BeginTransactionAsync();
        // Lock the owner first, matching creation, to serialize upper-name assignment and version allocation.
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT \"Id\" FROM \"Users\" WHERE \"Id\" = {userId} FOR UPDATE");
        var beyblade = await db.Beyblades.WithConfiguration().SingleOrDefaultAsync(x => x.Id == beybladeId && x.UserId == userId && !x.IsDeleted);
        if (beyblade is null) return ServiceResult.Failure("找不到陀螺。");

        if (customName is not null)
        {
            customName = customName.Trim();
            if (customName.Length is < 1 or > 100)
                return ServiceResult.Failure("陀螺名稱為必填，且最多 100 個字元。");
            if (await db.Beyblades.AnyAsync(x => x.UserId == userId && x.Id != beybladeId && x.Name == customName))
                return ServiceResult.Failure("已有相同名稱的陀螺。");
        }

        var parts = await db.Parts.AsNoTracking().Where(x => partIds.Contains(x.Id)).ToListAsync();
        if (parts.Count != partIds.Count) return ServiceResult.Failure("找不到所選零件。");
        var error = BeybladeAssemblyRules.Validate(parts);
        if (error is not null) return ServiceResult.Failure(error);

        var configuration = new BeybladeConfiguration
        {
            BeybladeId = beybladeId,
            VersionNo = beyblade.Configurations.Select(x => x.VersionNo).DefaultIfEmpty(0).Max() + 1,
            PartsKey = string.Join(",", partIds.Order()),
            CreatedAtUtc = DateTime.UtcNow,
            Parts = parts.Select(x => new BeybladeConfigurationPart { PartId = x.Id, Part = x, PartNameSnapshot = x.Name }).ToList()
        };
        var upperName = configuration.UpperName;
        var currentUpper = beyblade.UpperName ?? beyblade.Configuration?.UpperName;
        if (currentUpper is not null && currentUpper != upperName)
            return ServiceResult.Failure("上蓋名稱不同，請建立另一顆陀螺。");
        if (await db.Beyblades.AnyAsync(x => x.UserId == userId && x.Id != beybladeId && !x.IsDeleted && x.UpperName == upperName))
            return ServiceResult.Failure("已有相同上蓋的陀螺，請在該陀螺新增版本；舊陀螺的歷史不會自動合併。");
        beyblade.UpperName = upperName;
        if (customName is not null)
        {
            beyblade.Name = customName;
            beyblade.UpdatedAtUtc = DateTime.UtcNow;
        }
        if (!beyblade.Configurations.Any(x => x.PartsKey == configuration.PartsKey))
        {
            // Catalog rows already exist; add only the snapshot references.
            foreach (var item in configuration.Parts) item.Part = null!;
            beyblade.Configurations.Add(configuration);
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ServiceResult.Success();
    }
}
