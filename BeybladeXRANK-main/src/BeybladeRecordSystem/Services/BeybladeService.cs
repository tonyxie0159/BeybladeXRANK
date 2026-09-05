using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain;
using BeybladeRecordSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Services;

public class BeybladeService(AppDbContext db)
{
    public Task<List<Beyblade>> GetMyBeybladesAsync(int userId) =>
        db.Beyblades.AsNoTracking().WithConfiguration().Where(x => x.UserId == userId && !x.IsDeleted).OrderBy(x => x.Name).ToListAsync();

    public async Task<ServiceResult> CreateAsync(int userId, string name, IReadOnlyList<int> partIds)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Users\" WHERE \"Id\" = {userId} FOR UPDATE");
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100) return ServiceResult.Failure("陀螺名稱為必填，且最多 100 個字元。");
        if (await db.Beyblades.AnyAsync(x => x.UserId == userId && x.Name == name)) return ServiceResult.Failure("已有相同名稱的陀螺。");
        if (partIds.Count is < 2 or > 6 || partIds.Any(x => x <= 0) || partIds.Distinct().Count() != partIds.Count)
            return ServiceResult.Failure("請選擇完整且不重複的零件。");
        var parts = await db.Parts.AsNoTracking().Where(x => partIds.Contains(x.Id)).ToListAsync();
        if (parts.Count != partIds.Count) return ServiceResult.Failure("找不到所選零件。");
        var error = BeybladeAssemblyRules.Validate(parts);
        if (error is not null) return ServiceResult.Failure(error);
        var upperName = BeybladeNaming.UpperName(parts.Select(x => new BeybladeConfigurationPart { Part = x, PartNameSnapshot = x.Name }));
        if (await db.Beyblades.AnyAsync(x => x.UserId == userId && !x.IsDeleted && x.UpperName == upperName))
            return ServiceResult.Failure("已有相同上蓋的陀螺，請回到我的陀螺，在該陀螺新增版本。");
        var now = DateTime.UtcNow;
        db.Beyblades.Add(new Beyblade
        {
            UserId = userId, Name = name, UpperName = upperName, CreatedAtUtc = now, UpdatedAtUtc = now,
            Configuration = new BeybladeConfiguration
            {
                CreatedAtUtc = now,
                PartsKey = string.Join(",", partIds.Order()),
                Parts = parts.Select(x => new BeybladeConfigurationPart { PartId = x.Id, PartNameSnapshot = x.Name }).ToList()
            }
        });
        // EF saves the Beyblade and its complete configuration in a single transaction.
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> RenameAsync(int userId, int beybladeId, string name)
    {
        var beyblade = await db.Beyblades.SingleOrDefaultAsync(x => x.Id == beybladeId && x.UserId == userId && !x.IsDeleted);
        if (beyblade is null) return ServiceResult.Failure("找不到陀螺。");
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100) return ServiceResult.Failure("陀螺名稱為必填，且最多 100 個字元。");
        if (await db.Beyblades.AnyAsync(x => x.UserId == userId && x.Id != beybladeId && x.Name == name)) return ServiceResult.Failure("已有相同名稱的陀螺。");
        beyblade.Name = name;
        beyblade.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> DeleteAsync(int userId, int beybladeId)
    {
        var beyblade = await db.Beyblades.SingleOrDefaultAsync(x => x.Id == beybladeId && x.UserId == userId && !x.IsDeleted);
        if (beyblade is null) return ServiceResult.Failure("找不到陀螺。");
        beyblade.IsDeleted = true;
        beyblade.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return ServiceResult.Success();
    }
}
