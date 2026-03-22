using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Infrastructure;

namespace Tracker.Gateway.UnitTests;

public sealed class AnnounceRequestParserEdgeCaseTests
{
    private readonly AnnounceRequestParser _parser = new(Options.Create(new TrackerSecurityOptions
    {
        AllowIPv6Peers = true
    }));

    [Fact]
    public void TryParse_MissingQueryString_ReturnsError()
    {
        var httpContext = CreateHttpContext();
        httpContext.Request.QueryString = QueryString.Empty;

        var parsed = _parser.TryParse(httpContext, null, out _, out var error);

        Assert.False(parsed);
        Assert.Contains("missing query string", error.FailureReason);
    }

    [Fact]
    public void TryParse_MissingPeerId_ReturnsError()
    {
        var httpContext = CreateHttpContext();
        httpContext.Request.QueryString = new QueryString("?info_hash=%01%02%03%04%05%06%07%08%09%0A%0B%0C%0D%0E%0F%10%11%12%13%14&port=6881");

        var parsed = _parser.TryParse(httpContext, null, out _, out var error);

        Assert.False(parsed);
        Assert.Contains("missing peer_id", error.FailureReason);
    }

    [Fact]
    public void TryParse_PortZero_ReturnsError()
    {
        var httpContext = CreateHttpContext();
        httpContext.Request.QueryString = new QueryString(
            "?info_hash=%01%02%03%04%05%06%07%08%09%0A%0B%0C%0D%0E%0F%10%11%12%13%14" +
            "&peer_id=%01%02%03%04%05%06%07%08%09%0A%0B%0C%0D%0E%0F%10%11%12%13%14" +
            "&port=0&uploaded=0&downloaded=0&left=0");

        var parsed = _parser.TryParse(httpContext, null, out _, out var error);

        Assert.False(parsed);
        Assert.Contains("port", error.FailureReason);
    }

    [Fact]
    public void TryParse_InvalidPortNonNumeric_ReturnsError()
    {
        var httpContext = CreateHttpContext();
        httpContext.Request.QueryString = new QueryString(
            "?info_hash=%01%02%03%04%05%06%07%08%09%0A%0B%0C%0D%0E%0F%10%11%12%13%14" +
            "&peer_id=%01%02%03%04%05%06%07%08%09%0A%0B%0C%0D%0E%0F%10%11%12%13%14" +
            "&port=abc&uploaded=0&downloaded=0&left=0");

        var parsed = _parser.TryParse(httpContext, null, out _, out var error);

        Assert.False(parsed);
        Assert.Contains("port", error.FailureReason);
    }

    [Fact]
    public void TryParse_InvalidUploadedNonNumeric_ReturnsError()
    {
        var httpContext = CreateHttpContext();
        httpContext.Request.QueryString = new QueryString(
            "?info_hash=%01%02%03%04%05%06%07%08%09%0A%0B%0C%0D%0E%0F%10%11%12%13%14" +
            "&peer_id=%01%02%03%04%05%06%07%08%09%0A%0B%0C%0D%0E%0F%10%11%12%13%14" +
            "&port=6881&uploaded=abc&downloaded=0&left=0");

        var parsed = _parser.TryParse(httpContext, null, out _, out var error);

        Assert.False(parsed);
        Assert.Contains("uploaded", error.FailureReason);
    }

    [Fact]
    public void TryParse_Compact0_ParsesAsNonCompact()
    {
        var httpContext = CreateHttpContext();
        httpContext.Request.QueryString = new QueryString(
            "?info_hash=%01%02%03%04%05%06%07%08%09%0A%0B%0C%0D%0E%0F%10%11%12%13%14" +
            "&peer_id=%01%02%03%04%05%06%07%08%09%0A%0B%0C%0D%0E%0F%10%11%12%13%14" +
            "&port=6881&uploaded=0&downloaded=0&left=0&compact=0");

        var parsed = _parser.TryParse(httpContext, null, out var request, out _);

        Assert.True(parsed);
        Assert.False(request.Compact);
    }

    [Fact]
    public void TryParse_EventCompleted_ParsesCorrectly()
    {
        var httpContext = CreateHttpContext();
        httpContext.Request.QueryString = new QueryString(
            "?info_hash=%01%02%03%04%05%06%07%08%09%0A%0B%0C%0D%0E%0F%10%11%12%13%14" +
            "&peer_id=%01%02%03%04%05%06%07%08%09%0A%0B%0C%0D%0E%0F%10%11%12%13%14" +
            "&port=6881&uploaded=0&downloaded=0&left=0&event=completed");

        var parsed = _parser.TryParse(httpContext, null, out var request, out _);

        Assert.True(parsed);
        Assert.Equal(TrackerEvent.Completed, request.Event);
    }

    [Fact]
    public void TryParse_EventStopped_ParsesCorrectly()
    {
        var httpContext = CreateHttpContext();
        httpContext.Request.QueryString = new QueryString(
            "?info_hash=%01%02%03%04%05%06%07%08%09%0A%0B%0C%0D%0E%0F%10%11%12%13%14" +
            "&peer_id=%01%02%03%04%05%06%07%08%09%0A%0B%0C%0D%0E%0F%10%11%12%13%14" +
            "&port=6881&uploaded=0&downloaded=0&left=0&event=stopped");

        var parsed = _parser.TryParse(httpContext, null, out var request, out _);

        Assert.True(parsed);
        Assert.Equal(TrackerEvent.Stopped, request.Event);
    }

    [Fact]
    public void TryParse_UnknownEvent_ParsesAsNone()
    {
        var httpContext = CreateHttpContext();
        httpContext.Request.QueryString = new QueryString(
            "?info_hash=%01%02%03%04%05%06%07%08%09%0A%0B%0C%0D%0E%0F%10%11%12%13%14" +
            "&peer_id=%01%02%03%04%05%06%07%08%09%0A%0B%0C%0D%0E%0F%10%11%12%13%14" +
            "&port=6881&uploaded=0&downloaded=0&left=0&event=unknown");

        var parsed = _parser.TryParse(httpContext, null, out var request, out _);

        Assert.True(parsed);
        Assert.Equal(TrackerEvent.None, request.Event);
    }

    [Fact]
    public void TryParse_PasskeyFromRoute_TakesPrecedence()
    {
        var httpContext = CreateHttpContext();
        httpContext.Request.QueryString = new QueryString(
            "?info_hash=%01%02%03%04%05%06%07%08%09%0A%0B%0C%0D%0E%0F%10%11%12%13%14" +
            "&peer_id=%01%02%03%04%05%06%07%08%09%0A%0B%0C%0D%0E%0F%10%11%12%13%14" +
            "&port=6881&uploaded=0&downloaded=0&left=0&passkey=query-pk");

        var parsed = _parser.TryParse(httpContext, "route-pk", out var request, out _);

        Assert.True(parsed);
        Assert.Equal("route-pk", request.Passkey);
    }

    [Fact]
    public void TryParse_PasskeyFromQueryFallback()
    {
        var httpContext = CreateHttpContext();
        httpContext.Request.QueryString = new QueryString(
            "?info_hash=%01%02%03%04%05%06%07%08%09%0A%0B%0C%0D%0E%0F%10%11%12%13%14" +
            "&peer_id=%01%02%03%04%05%06%07%08%09%0A%0B%0C%0D%0E%0F%10%11%12%13%14" +
            "&port=6881&uploaded=0&downloaded=0&left=0&passkey=query-pk");

        var parsed = _parser.TryParse(httpContext, null, out var request, out _);

        Assert.True(parsed);
        Assert.Equal("query-pk", request.Passkey);
    }

    [Fact]
    public void TryParse_PortOverridesRemotePort()
    {
        var httpContext = CreateHttpContext(remotePort: 9999);
        httpContext.Request.QueryString = new QueryString(
            "?info_hash=%01%02%03%04%05%06%07%08%09%0A%0B%0C%0D%0E%0F%10%11%12%13%14" +
            "&peer_id=%01%02%03%04%05%06%07%08%09%0A%0B%0C%0D%0E%0F%10%11%12%13%14" +
            "&port=51413&uploaded=0&downloaded=0&left=0");

        var parsed = _parser.TryParse(httpContext, null, out var request, out _);

        Assert.True(parsed);
        Assert.Equal(51413, request.Endpoint.Port);
    }

    private static DefaultHttpContext CreateHttpContext(string ip = "127.0.0.1", int remotePort = 51413)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        httpContext.Connection.RemotePort = remotePort;
        return httpContext;
    }
}
