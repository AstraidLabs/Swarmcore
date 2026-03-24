using BeeTracker.Contracts.Runtime;

namespace Tracker.Cluster.UnitTests;

// ─── In-Process Operational State Store ─────────────────────────────────────

/// <summary>In-memory node operational state store for tests.</summary>
internal sealed class InMemoryNodeOperationalStateStore
    : Tracker.Gateway.Application.Cluster.INodeOperationalStateStore
{
    private readonly Dictionary<string, NodeOperationalStateDto> _store = [];
    private readonly object _lock = new();

    public Task<NodeOperationalStateDto?> GetStateAsync(string nodeId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _store.TryGetValue(nodeId, out var dto);
            return Task.FromResult(dto);
        }
    }

    public Task SetStateAsync(NodeOperationalStateDto state, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _store[state.NodeId] = state;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<NodeOperationalStateDto>> GetAllStatesAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyCollection<NodeOperationalStateDto>>(
                _store.Values.ToArray());
        }
    }
}

// ─── Node Operational State Tests ─────────────────────────────────────────────

public sealed class NodeOperationalStateTests
{
    [Fact]
    public async Task GetState_WhenNeverSet_ReturnsNull()
    {
        var store = new InMemoryNodeOperationalStateStore();
        var state = await store.GetStateAsync("node-1", CancellationToken.None);
        Assert.Null(state);
    }

    [Fact]
    public async Task SetState_Active_CanBeRead()
    {
        var store = new InMemoryNodeOperationalStateStore();
        var dto = new NodeOperationalStateDto("node-1", NodeOperationalState.Active, DateTimeOffset.UtcNow);
        await store.SetStateAsync(dto, CancellationToken.None);

        var read = await store.GetStateAsync("node-1", CancellationToken.None);
        Assert.NotNull(read);
        Assert.Equal(NodeOperationalState.Active, read!.State);
    }

    [Fact]
    public async Task SetState_Draining_CanBeRead()
    {
        var store = new InMemoryNodeOperationalStateStore();
        var dto = new NodeOperationalStateDto("node-1", NodeOperationalState.Draining, DateTimeOffset.UtcNow);
        await store.SetStateAsync(dto, CancellationToken.None);

        var read = await store.GetStateAsync("node-1", CancellationToken.None);
        Assert.NotNull(read);
        Assert.Equal(NodeOperationalState.Draining, read!.State);
    }

    [Fact]
    public async Task SetState_Maintenance_CanBeRead()
    {
        var store = new InMemoryNodeOperationalStateStore();
        var dto = new NodeOperationalStateDto("node-1", NodeOperationalState.Maintenance, DateTimeOffset.UtcNow);
        await store.SetStateAsync(dto, CancellationToken.None);

        var read = await store.GetStateAsync("node-1", CancellationToken.None);
        Assert.NotNull(read);
        Assert.Equal(NodeOperationalState.Maintenance, read!.State);
    }

    [Fact]
    public async Task SetState_Transition_DrainToActive()
    {
        var store = new InMemoryNodeOperationalStateStore();

        // Move to draining
        await store.SetStateAsync(new NodeOperationalStateDto("node-1", NodeOperationalState.Draining, DateTimeOffset.UtcNow), CancellationToken.None);
        var draining = await store.GetStateAsync("node-1", CancellationToken.None);
        Assert.Equal(NodeOperationalState.Draining, draining!.State);

        // Return to active (e.g., drain canceled)
        await store.SetStateAsync(new NodeOperationalStateDto("node-1", NodeOperationalState.Active, DateTimeOffset.UtcNow), CancellationToken.None);
        var active = await store.GetStateAsync("node-1", CancellationToken.None);
        Assert.Equal(NodeOperationalState.Active, active!.State);
    }

    [Fact]
    public async Task GetAllStates_MultipleNodes()
    {
        var store = new InMemoryNodeOperationalStateStore();
        await store.SetStateAsync(new NodeOperationalStateDto("node-1", NodeOperationalState.Active, DateTimeOffset.UtcNow), CancellationToken.None);
        await store.SetStateAsync(new NodeOperationalStateDto("node-2", NodeOperationalState.Draining, DateTimeOffset.UtcNow), CancellationToken.None);
        await store.SetStateAsync(new NodeOperationalStateDto("node-3", NodeOperationalState.Maintenance, DateTimeOffset.UtcNow), CancellationToken.None);

        var all = await store.GetAllStatesAsync(CancellationToken.None);
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task GetAllStates_NoNodes_ReturnsEmpty()
    {
        var store = new InMemoryNodeOperationalStateStore();
        var all = await store.GetAllStatesAsync(CancellationToken.None);
        Assert.Empty(all);
    }

    [Theory]
    [InlineData(NodeOperationalState.Draining)]
    [InlineData(NodeOperationalState.Maintenance)]
    public async Task IsDrainingOrMaintenance_WhenSet_ReadinessProbeLogicRejectsNode(NodeOperationalState state)
    {
        var store = new InMemoryNodeOperationalStateStore();
        await store.SetStateAsync(new NodeOperationalStateDto("node-1", state, DateTimeOffset.UtcNow), CancellationToken.None);

        var nodeState = await store.GetStateAsync("node-1", CancellationToken.None);
        Assert.NotNull(nodeState);

        // Simulate readiness probe check
        var shouldFailReadiness = nodeState!.State is NodeOperationalState.Draining or NodeOperationalState.Maintenance;
        Assert.True(shouldFailReadiness);
    }

    [Fact]
    public async Task IsActive_ReadinessProbePasses()
    {
        var store = new InMemoryNodeOperationalStateStore();
        await store.SetStateAsync(new NodeOperationalStateDto("node-1", NodeOperationalState.Active, DateTimeOffset.UtcNow), CancellationToken.None);

        var nodeState = await store.GetStateAsync("node-1", CancellationToken.None);
        var shouldFailReadiness = nodeState is { State: NodeOperationalState.Draining or NodeOperationalState.Maintenance };
        Assert.False(shouldFailReadiness);
    }
}

// ─── Operational State Enum Tests ─────────────────────────────────────────────

public sealed class NodeOperationalStateEnumTests
{
    [Fact]
    public void Active_HasValueZero_IsDefaultState()
    {
        // Default(NodeOperationalState) should be Active so un-initialized nodes are treated as active
        Assert.Equal(NodeOperationalState.Active, default(NodeOperationalState));
        Assert.Equal(0, (int)NodeOperationalState.Active);
    }

    [Fact]
    public void AllStates_HaveDistinctValues()
    {
        var values = Enum.GetValues<NodeOperationalState>();
        var distinct = values.Distinct().ToArray();
        Assert.Equal(values.Length, distinct.Length);
    }
}
