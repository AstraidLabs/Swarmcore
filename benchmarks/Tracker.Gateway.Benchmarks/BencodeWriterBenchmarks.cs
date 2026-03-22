using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Infrastructure;

namespace Tracker.Gateway.Benchmarks;

[MemoryDiagnoser]
public class BencodeWriterBenchmarks
{
    private readonly AnnounceBencodeResponseWriter _writer = new();
    private readonly DefaultHttpContext _context = new();
    private AnnounceSuccess _success;
    private SelectedPeer[] _peers = null!;

    [GlobalSetup]
    public void Setup()
    {
        _peers = Enumerable.Range(0, 50)
            .Select(index => new SelectedPeer(PeerEndpoint.FromIPv4(0x7F000001, (ushort)(5000 + index))))
            .ToArray();

        _success = new AnnounceSuccess(1800, 25, 25, new AnnouncePeerSelection(new PeerSelectionResult(_peers, _peers.Length), default));
    }

    [Benchmark]
    public async Task<int> WriteResponse()
    {
        await using var body = new MemoryStream();
        _context.Response.Body = body;
        await _writer.WriteAnnounceSuccessAsync(_context.Response, _success, CancellationToken.None);
        return (int)body.Length;
    }
}
