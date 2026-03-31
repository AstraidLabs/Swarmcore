using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Tracker.AdminService.Api.Hubs;

[Authorize]
public sealed class LiveStatsHub : Hub
{
    public const string DashboardGroup = "Dashboard";

    public Task JoinDashboard()
        => Groups.AddToGroupAsync(Context.ConnectionId, DashboardGroup);

    public Task LeaveDashboard()
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, DashboardGroup);

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, DashboardGroup);
        await base.OnConnectedAsync();
    }
}
