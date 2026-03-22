namespace ArchitectureTests;

public sealed class DependencyFlowTests
{
    [Fact]
    public void SolutionAssembliesLoad()
    {
        Assert.True(typeof(Tracker.Gateway.Application.Announce.IAnnounceService).Assembly.GetName().Name is not null);
    }
}
