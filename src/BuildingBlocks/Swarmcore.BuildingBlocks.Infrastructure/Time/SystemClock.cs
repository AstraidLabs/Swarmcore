using Swarmcore.BuildingBlocks.Abstractions.Time;

namespace Swarmcore.BuildingBlocks.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
