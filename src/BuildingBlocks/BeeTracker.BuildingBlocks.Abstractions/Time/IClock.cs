namespace BeeTracker.BuildingBlocks.Abstractions.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
