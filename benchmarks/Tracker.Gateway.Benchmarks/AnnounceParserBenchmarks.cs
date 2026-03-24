using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Net;
using BeeTracker.BuildingBlocks.Abstractions.Options;
using Tracker.Gateway.Infrastructure;

namespace Tracker.Gateway.Benchmarks;

[MemoryDiagnoser]
public class AnnounceParserBenchmarks
{
    private readonly AnnounceRequestParser _parser = new(Options.Create(new TrackerSecurityOptions
    {
        AllowIPv6Peers = true
    }));
    private DefaultHttpContext _httpContext = null!;

    [GlobalSetup]
    public void Setup()
    {
        _httpContext = new DefaultHttpContext();
        _httpContext.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        _httpContext.Connection.RemotePort = 51413;
        _httpContext.Request.QueryString = new QueryString("?info_hash=%01%02%03%04%05%06%07%08%09%0A%0B%0C%0D%0E%0F%10%11%12%13%14&peer_id=-UT0001-%01%02%03%04%05%06%07%08%09%0A%0B%0C&port=51413&uploaded=11&downloaded=22&left=33&compact=1&event=started&numwant=25");
    }

    [Benchmark]
    public bool Parse() => _parser.TryParse(_httpContext, "pk-1", out _, out _);
}
