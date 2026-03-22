using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Infrastructure;

namespace Tracker.Gateway.UnitTests;

public sealed class AnnounceRequestParserTests
{
    private readonly AnnounceRequestParser _parser = new(Options.Create(new TrackerSecurityOptions
    {
        AllowIPv6Peers = true
    }));

    [Fact]
    public void TryParse_DecodesTrackerQuery()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        httpContext.Connection.RemotePort = 51413;
        httpContext.Request.QueryString = new QueryString("?info_hash=%01%02%03%04%05%06%07%08%09%0A%0B%0C%0D%0E%0F%10%11%12%13%14&peer_id=-UT0001-%01%02%03%04%05%06%07%08%09%0A%0B%0C&port=51413&uploaded=11&downloaded=22&left=33&compact=1&event=started&numwant=25");

        var parsed = _parser.TryParse(httpContext, "pk-1", out var request, out var error);

        Assert.True(parsed);
        Assert.Equal(default, error);
        Assert.Equal("0102030405060708090A0B0C0D0E0F1011121314", request.InfoHash.ToHexString());
        Assert.Equal(51413, request.Endpoint.Port);
        Assert.Equal(11, request.Uploaded);
        Assert.Equal(22, request.Downloaded);
        Assert.Equal(33, request.Left);
        Assert.Equal(25, request.RequestedPeers);
        Assert.Equal(TrackerEvent.Started, request.Event);
        Assert.Equal("pk-1", request.Passkey);
    }

    [Fact]
    public void TryParse_RejectsMissingInfoHash()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        httpContext.Connection.RemotePort = 51413;
        httpContext.Request.QueryString = new QueryString("?peer_id=-UT0001-%01%02%03%04%05%06%07%08%09%0A%0B%0C%0D&port=51413");

        var parsed = _parser.TryParse(httpContext, null, out _, out var error);

        Assert.False(parsed);
        Assert.Equal(StatusCodes.Status400BadRequest, error.StatusCode);
        Assert.Equal("missing info_hash", error.FailureReason);
    }
}
