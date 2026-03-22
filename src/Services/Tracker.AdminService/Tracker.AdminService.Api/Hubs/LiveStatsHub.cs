using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Tracker.AdminService.Api.Hubs;

[Authorize]
public sealed class LiveStatsHub : Hub
{
}
