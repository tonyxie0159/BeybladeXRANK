using Microsoft.AspNetCore.SignalR;

namespace BeybladeRecordSystem.Realtime;

public interface IRealtimePublisher
{
    Task PublishUserAsync(int userId, string eventType, object payload, CancellationToken cancellationToken = default);
    Task PublishUsersAsync(IEnumerable<int> userIds, string eventType, object payload, CancellationToken cancellationToken = default);
}

public sealed class RealtimePublisher(IHubContext<RealtimeHub> hubContext) : IRealtimePublisher
{
    public Task PublishUserAsync(int userId, string eventType, object payload, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(RealtimeHub.UserGroup(userId))
            .SendAsync("RealtimeEvent", new { eventType, payload }, cancellationToken);

    public Task PublishUsersAsync(IEnumerable<int> userIds, string eventType, object payload, CancellationToken cancellationToken = default)
    {
        var groups = userIds.Distinct().Select(RealtimeHub.UserGroup).ToList();
        return groups.Count == 0
            ? Task.CompletedTask
            : hubContext.Clients.Groups(groups).SendAsync("RealtimeEvent", new { eventType, payload }, cancellationToken);
    }
}
