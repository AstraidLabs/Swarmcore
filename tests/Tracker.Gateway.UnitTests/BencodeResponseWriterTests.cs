using Microsoft.AspNetCore.Http;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Infrastructure;

namespace Tracker.Gateway.UnitTests;

public sealed class BencodeResponseWriterTests
{
    [Fact]
    public async Task WriteFailureAsync_WritesTrackerFailureDictionary()
    {
        var httpContext = new DefaultHttpContext();
        await using var body = new MemoryStream();
        httpContext.Response.Body = body;

        var writer = new AnnounceBencodeResponseWriter();
        await writer.WriteFailureAsync(httpContext.Response, StatusCodes.Status400BadRequest, "bad request", CancellationToken.None);

        var payload = body.ToArray();
        var text = System.Text.Encoding.ASCII.GetString(payload);

        Assert.Equal("d14:failure reason11:bad requeste", text);
    }

    [Fact]
    public async Task WriteAnnounceSuccessAsync_WritesCompactPeerPayload()
    {
        var httpContext = new DefaultHttpContext();
        await using var body = new MemoryStream();
        httpContext.Response.Body = body;

        var peers = new[] { new SelectedPeer(PeerEndpoint.FromIPv4(0x7F000001, 51413)) };
        using var pooled = new PeerSelectionResult(peers, 1);
        var writer = new AnnounceBencodeResponseWriter();

        await writer.WriteAnnounceSuccessAsync(
            httpContext.Response,
            new AnnounceSuccess(1800, 5, 3, new AnnouncePeerSelection(pooled, default)),
            CancellationToken.None);

        var payload = body.ToArray();
        Assert.True(ContainsSubsequence(payload, System.Text.Encoding.ASCII.GetBytes("8:completei5e")));
        Assert.True(ContainsSubsequence(payload, System.Text.Encoding.ASCII.GetBytes("10:incompletei3e")));
        Assert.True(ContainsSubsequence(payload, System.Text.Encoding.ASCII.GetBytes("8:intervali1800e")));
        Assert.True(ContainsSubsequence(payload, [127, 0, 0, 1, 200, 213]));
    }

    [Fact]
    public async Task WriteAnnounceSuccessAsync_WritesPeers6Payload()
    {
        var httpContext = new DefaultHttpContext();
        await using var body = new MemoryStream();
        httpContext.Response.Body = body;

        var peers6 = new[]
        {
            new SelectedPeer(PeerEndpoint.FromIPv6(Convert.FromHexString("20010DB8000000000000000000000001"), 51413))
        };

        using var pooled6 = new PeerSelectionResult(peers6, 1);
        var writer = new AnnounceBencodeResponseWriter();

        await writer.WriteAnnounceSuccessAsync(
            httpContext.Response,
            new AnnounceSuccess(1800, 2, 1, new AnnouncePeerSelection(default, pooled6)),
            CancellationToken.None);

        var payload = body.ToArray();
        Assert.True(ContainsSubsequence(payload, System.Text.Encoding.ASCII.GetBytes("6:peers6")));
        Assert.True(ContainsSubsequence(payload, [0x20, 0x01, 0x0d, 0xb8]));
    }

    [Fact]
    public async Task WriteScrapeSuccessAsync_WritesFilesDictionary()
    {
        var httpContext = new DefaultHttpContext();
        await using var body = new MemoryStream();
        httpContext.Response.Body = body;

        var writer = new AnnounceBencodeResponseWriter();
        var infoHash = InfoHashKey.FromBytes(Convert.FromHexString("0102030405060708090A0B0C0D0E0F1011121314"));

        await writer.WriteScrapeSuccessAsync(
            httpContext.Response,
            new ScrapeSuccess([new ScrapeFileEntry(infoHash, 5, 3, 7)]),
            CancellationToken.None);

        var payload = body.ToArray();
        var text = System.Text.Encoding.ASCII.GetString(payload);
        Assert.Contains("5:files", text);
        Assert.Contains("8:completei5e", text);
        Assert.Contains("10:downloadedi7e", text);
        Assert.Contains("10:incompletei3e", text);
    }

    [Fact]
    public async Task WriteAnnounceSuccessAsync_NonCompact_WritesPeerDictionaries()
    {
        var httpContext = new DefaultHttpContext();
        await using var body = new MemoryStream();
        httpContext.Response.Body = body;

        var peerId = PeerIdKey.FromBytes(Convert.FromHexString("4142434445464748494A4B4C4D4E4F5051525354"));
        var peers = new[] { new SelectedPeer(PeerEndpoint.FromIPv4(0x0A000001, 6881), peerId) };
        using var pooled = new PeerSelectionResult(peers, 1);
        var writer = new AnnounceBencodeResponseWriter();

        await writer.WriteAnnounceSuccessAsync(
            httpContext.Response,
            new AnnounceSuccess(1800, 2, 1, new AnnouncePeerSelection(pooled, default), Compact: false),
            CancellationToken.None);

        var payload = body.ToArray();
        var text = System.Text.Encoding.ASCII.GetString(payload);

        Assert.Contains("5:peers", text);
        Assert.Contains("2:ip", text);
        Assert.Contains("7:peer id", text);
        Assert.Contains("4:port", text);
        Assert.Contains("i6881e", text);
    }

    [Fact]
    public async Task WriteAnnounceSuccessAsync_WarningMessage_WritesWarningKey()
    {
        var httpContext = new DefaultHttpContext();
        await using var body = new MemoryStream();
        httpContext.Response.Body = body;

        var writer = new AnnounceBencodeResponseWriter();

        await writer.WriteAnnounceSuccessAsync(
            httpContext.Response,
            new AnnounceSuccess(1800, 0, 0, default, WarningMessage: "test warning"),
            CancellationToken.None);

        var payload = body.ToArray();
        var text = System.Text.Encoding.ASCII.GetString(payload);

        Assert.Contains("15:warning message", text);
        Assert.Contains("12:test warning", text);
    }

    [Fact]
    public async Task WriteAnnounceSuccessAsync_NoWarningMessage_OmitsWarningKey()
    {
        var httpContext = new DefaultHttpContext();
        await using var body = new MemoryStream();
        httpContext.Response.Body = body;

        var writer = new AnnounceBencodeResponseWriter();

        await writer.WriteAnnounceSuccessAsync(
            httpContext.Response,
            new AnnounceSuccess(1800, 0, 0, default),
            CancellationToken.None);

        var payload = body.ToArray();
        var text = System.Text.Encoding.ASCII.GetString(payload);

        Assert.DoesNotContain("warning message", text);
    }

    private static bool ContainsSubsequence(ReadOnlySpan<byte> source, ReadOnlySpan<byte> value)
    {
        return source.IndexOf(value) >= 0;
    }
}
