using Swarmcore.BuildingBlocks.Abstractions.Hosting;

namespace Swarmcore.BuildingBlocks.Infrastructure.Hosting;

public sealed class ReadinessState : IReadinessState
{
    private int _ready;

    public bool IsReady => Volatile.Read(ref _ready) == 1;

    public void MarkReady()
    {
        Interlocked.Exchange(ref _ready, 1);
    }
}
