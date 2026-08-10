using System.Security.Claims;

namespace BeybladeRecordSystem.Infrastructure;

public static class ClaimsPrincipalExtensions
{
    public static int GetRequiredUserId(this ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : throw new UnauthorizedAccessException("登入工作階段無效。");
}
