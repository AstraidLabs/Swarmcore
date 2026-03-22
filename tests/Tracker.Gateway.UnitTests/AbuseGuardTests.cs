using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Infrastructure;

namespace Tracker.Gateway.UnitTests;

public sealed class AbuseGuardTests
{
    [Fact]
    public void ScrapeAbuseGuard_WhenDisabled_ReturnsNull()
    {
        var guard = new ScrapeAbuseGuard(Options.Create(new TrackerAbuseProtectionOptions
        {
            EnableScrapeIpRateLimit = false
        }));

        var httpContext = CreateHttpContext("10.0.0.1");
        Assert.Null(guard.Evaluate(httpContext));
    }

    [Fact]
    public void ScrapeAbuseGuard_UnderLimit_ReturnsNull()
    {
        var guard = new ScrapeAbuseGuard(Options.Create(new TrackerAbuseProtectionOptions
        {
            EnableScrapeIpRateLimit = true,
            ScrapePerIpPerSecond = 5
        }));

        var httpContext = CreateHttpContext("10.0.0.2");
        for (var i = 0; i < 5; i++)
        {
            Assert.Null(guard.Evaluate(httpContext));
        }
    }

    [Fact]
    public void ScrapeAbuseGuard_OverLimit_ReturnsError()
    {
        var guard = new ScrapeAbuseGuard(Options.Create(new TrackerAbuseProtectionOptions
        {
            EnableScrapeIpRateLimit = true,
            ScrapePerIpPerSecond = 2
        }));

        var httpContext = CreateHttpContext("10.0.0.3");
        Assert.Null(guard.Evaluate(httpContext));
        Assert.Null(guard.Evaluate(httpContext));
        var error = guard.Evaluate(httpContext);

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status429TooManyRequests, error.Value.StatusCode);
    }

    [Fact]
    public void ScrapeAbuseGuard_DifferentIps_IndependentLimits()
    {
        var guard = new ScrapeAbuseGuard(Options.Create(new TrackerAbuseProtectionOptions
        {
            EnableScrapeIpRateLimit = true,
            ScrapePerIpPerSecond = 1
        }));

        var ctx1 = CreateHttpContext("10.0.0.4");
        var ctx2 = CreateHttpContext("10.0.0.5");

        Assert.Null(guard.Evaluate(ctx1));
        Assert.Null(guard.Evaluate(ctx2));
        Assert.NotNull(guard.Evaluate(ctx1));
        Assert.NotNull(guard.Evaluate(ctx2));
    }

    [Fact]
    public void AnnounceAbuseGuard_IpRateLimit_WhenDisabled_DoesNotThrottle()
    {
        var guard = new AnnounceAbuseGuard(Options.Create(new TrackerAbuseProtectionOptions
        {
            EnableAnnouncePasskeyRateLimit = false,
            EnableAnnounceIpRateLimit = false
        }));

        var httpContext = CreateHttpContext("10.0.0.6");
        var request = CreateAnnounceRequest(null);

        for (var i = 0; i < 100; i++)
        {
            Assert.Null(guard.Evaluate(httpContext, request));
        }
    }

    [Fact]
    public void AnnounceAbuseGuard_IpRateLimit_WhenEnabled_ThrottlesOverLimit()
    {
        var guard = new AnnounceAbuseGuard(Options.Create(new TrackerAbuseProtectionOptions
        {
            EnableAnnouncePasskeyRateLimit = false,
            EnableAnnounceIpRateLimit = true,
            AnnouncePerIpPerSecond = 2
        }));

        var httpContext = CreateHttpContext("10.0.0.7");
        var request = CreateAnnounceRequest(null);

        Assert.Null(guard.Evaluate(httpContext, request));
        Assert.Null(guard.Evaluate(httpContext, request));
        var error = guard.Evaluate(httpContext, request);

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status429TooManyRequests, error.Value.StatusCode);
    }

    [Fact]
    public void AnnounceAbuseGuard_PasskeyRateLimit_ThrottlesOverLimit()
    {
        var guard = new AnnounceAbuseGuard(Options.Create(new TrackerAbuseProtectionOptions
        {
            EnableAnnouncePasskeyRateLimit = true,
            AnnouncePerPasskeyPerSecond = 1,
            EnableAnnounceIpRateLimit = false
        }));

        var httpContext = CreateHttpContext("10.0.0.8");
        var request = CreateAnnounceRequest("test-passkey");

        Assert.Null(guard.Evaluate(httpContext, request));
        var error = guard.Evaluate(httpContext, request);

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status429TooManyRequests, error.Value.StatusCode);
    }

    private static DefaultHttpContext CreateHttpContext(string ipAddress)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse(ipAddress);
        return httpContext;
    }

    private static AnnounceRequest CreateAnnounceRequest(string? passkey)
    {
        return new AnnounceRequest(
            InfoHashKey.FromBytes(new byte[20]),
            PeerIdKey.FromBytes(new byte[20]),
            PeerEndpoint.FromIPv4(0x7F000001, 6881),
            0, 0, 100, 50, true, TrackerEvent.Started, passkey);
    }
}
