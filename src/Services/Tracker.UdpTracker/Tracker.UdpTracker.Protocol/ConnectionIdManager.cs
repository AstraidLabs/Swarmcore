using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Tracker.UdpTracker.Protocol;

public sealed class ConnectionIdManager
{
    private readonly ConcurrentDictionary<long, long> _connections = new();
    private readonly int _ttlSeconds;

    public ConnectionIdManager(int ttlSeconds = 120)
    {
        _ttlSeconds = ttlSeconds;
    }

    public long Issue()
    {
        var id = GenerateRandomId();
        var expiresUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _ttlSeconds;
        _connections[id] = expiresUnix;
        return id;
    }

    public bool Validate(long connectionId)
    {
        if (!_connections.TryGetValue(connectionId, out var expiresUnix))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expiresUnix)
        {
            _connections.TryRemove(connectionId, out _);
            return false;
        }

        return true;
    }

    public void Sweep()
    {
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var pair in _connections)
        {
            if (nowUnix >= pair.Value)
            {
                _connections.TryRemove(pair.Key, out _);
            }
        }
    }

    public int ActiveCount => _connections.Count;

    private static long GenerateRandomId()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToInt64(bytes);
    }
}
