using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain;
using BeybladeRecordSystem.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Services;

public class AuthService(AppDbContext db)
{
    private readonly PasswordHasher<User> _passwordHasher = new();

    public async Task<ServiceResult> RegisterAsync(string account, string password, string displayName)
    {
        account = account.Trim();
        displayName = displayName.Trim();
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(displayName))
            return ServiceResult.Failure("帳號、密碼與顯示名稱皆為必填。");
        if (account.Length > 64 || displayName.Length > 64)
            return ServiceResult.Failure("帳號與顯示名稱最多 64 個字元。");
        var normalizedAccount = IdentityNormalizer.Normalize(account);
        var normalizedDisplayName = IdentityNormalizer.Normalize(displayName);
        if (await db.Users.AnyAsync(x => x.NormalizedAccount == normalizedAccount))
            return ServiceResult.Failure("此帳號已被使用。");
        if (await db.Users.AnyAsync(x => x.NormalizedDisplayName == normalizedDisplayName))
            return ServiceResult.Failure("此玩家名稱已被使用。");

        var user = new User
        {
            Account = account,
            NormalizedAccount = normalizedAccount,
            DisplayName = displayName,
            NormalizedDisplayName = normalizedDisplayName,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, password);
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            db.Entry(user).State = EntityState.Detached;
            return ServiceResult.Failure("帳號或玩家名稱已被使用，請更換後再試。");
        }
        return ServiceResult.Success();
    }

    public async Task<User?> LoginAsync(string account, string password)
    {
        var normalizedAccount = IdentityNormalizer.Normalize(account);
        var user = await db.Users.SingleOrDefaultAsync(x => x.NormalizedAccount == normalizedAccount);
        return user is not null && _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed ? user : null;
    }

    public async Task<ServiceResult> ChangeDisplayNameAsync(int userId, string displayName)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null) return ServiceResult.Failure("找不到使用者。");
        displayName = displayName.Trim();
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 64) return ServiceResult.Failure("顯示名稱為必填，且最多 64 個字元。");
        var normalizedDisplayName = IdentityNormalizer.Normalize(displayName);
        if (await db.Users.AnyAsync(x => x.Id != userId && x.NormalizedDisplayName == normalizedDisplayName))
            return ServiceResult.Failure("此玩家名稱已被使用。");
        user.DisplayName = displayName;
        user.NormalizedDisplayName = normalizedDisplayName;
        user.UpdatedAtUtc = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            db.Entry(user).Reload();
            return ServiceResult.Failure("此玩家名稱已被使用。");
        }
        return ServiceResult.Success();
    }
}
