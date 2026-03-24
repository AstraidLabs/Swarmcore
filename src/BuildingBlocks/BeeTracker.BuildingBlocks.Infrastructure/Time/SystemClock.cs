using BeeTracker.BuildingBlocks.Abstractions.Time;

namespace BeeTracker.BuildingBlocks.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
