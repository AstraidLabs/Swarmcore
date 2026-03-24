namespace BeeTracker.BuildingBlocks.Abstractions.Hosting;

public interface IReadinessState
{
    bool IsReady { get; }
    void MarkReady();
}
