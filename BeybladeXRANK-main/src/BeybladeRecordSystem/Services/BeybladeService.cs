using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Services;

public class BeybladeService(AppDbContext db)
{
    public Task<List<Beyblade>> GetMyBeybladesAsync(int userId) =>
        db.Beyblades.Where(x => x.UserId == userId && !x.IsDeleted).OrderBy(x => x.Name).ToListAsync();

    public async Task<ServiceResult> CreateAsync(int userId, string name)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100) return ServiceResult.Failure("陀螺名稱為必填，且最多 100 個字元。");
        if (await db.Beyblades.AnyAsync(x => x.UserId == userId && x.Name == name)) return ServiceResult.Failure("已有相同名稱的陀螺。");
        db.Beyblades.Add(new Beyblade { UserId = userId, Name = name, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
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
