using Swarmcore.Contracts.Runtime;

namespace Tracker.CacheCoordinator.Application;

public interface INodeHeartbeatRegistry
{
    Task PublishHeartbeatAsync(NodeHeartbeatDto heartbeat, CancellationToken cancellationToken);
}
