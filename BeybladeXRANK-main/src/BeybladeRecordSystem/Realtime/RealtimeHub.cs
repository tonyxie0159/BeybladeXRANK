using BeybladeRecordSystem.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BeybladeRecordSystem.Realtime;

[Authorize]
public sealed class RealtimeHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(Context.User!.GetRequiredUserId()));
        await base.OnConnectedAsync();
    }

    public static string UserGroup(int userId) => $"user:{userId}";
}
