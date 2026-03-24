namespace BeeTracker.BuildingBlocks.Abstractions.Caching;

public interface ILocalCache<TValue>
{
    bool TryGet(string key, out TValue value);
    void Set(string key, TValue value, TimeSpan ttl);
    void Remove(string key);
}
