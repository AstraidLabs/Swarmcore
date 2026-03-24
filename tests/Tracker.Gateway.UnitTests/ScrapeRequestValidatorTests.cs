using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using BeeTracker.BuildingBlocks.Abstractions.Options;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Infrastructure;

namespace Tracker.Gateway.UnitTests;

public sealed class ScrapeRequestValidatorTests
{
    [Fact]
    public void Validate_EmptyInfoHashes_ReturnsError()
    {
        var validator = CreateValidator(maxScrapeInfoHashes: 74);
        var request = new ScrapeRequest(null, []);

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Equal(StatusCodes.Status400BadRequest, result.Error.StatusCode);
        Assert.Contains("missing", result.Error.FailureReason);
    }

    [Fact]
    public void Validate_WithinLimit_ReturnsSuccess()
    {
        var validator = CreateValidator(maxScrapeInfoHashes: 3);
        var hashes = new[]
        {
            InfoHashKey.FromBytes(new byte[20]),
            InfoHashKey.FromBytes(new byte[20]),
            InfoHashKey.FromBytes(new byte[20])
        };
        var request = new ScrapeRequest(null, hashes);

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_OverLimit_ReturnsError()
    {
        var validator = CreateValidator(maxScrapeInfoHashes: 2);
        var hashes = new[]
        {
            InfoHashKey.FromBytes(new byte[20]),
            InfoHashKey.FromBytes(new byte[20]),
            InfoHashKey.FromBytes(new byte[20])
        };
        var request = new ScrapeRequest(null, hashes);

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Equal(StatusCodes.Status400BadRequest, result.Error.StatusCode);
        Assert.Contains("too many", result.Error.FailureReason);
    }

    [Fact]
    public void Validate_ExactlyAtLimit_ReturnsSuccess()
    {
        var validator = CreateValidator(maxScrapeInfoHashes: 2);
        var hashes = new[]
        {
            InfoHashKey.FromBytes(new byte[20]),
            InfoHashKey.FromBytes(new byte[20])
        };
        var request = new ScrapeRequest(null, hashes);

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    private static ScrapeRequestValidator CreateValidator(int maxScrapeInfoHashes)
    {
        return new ScrapeRequestValidator(Options.Create(new TrackerSecurityOptions
        {
            MaxScrapeInfoHashes = maxScrapeInfoHashes
        }));
    }
}
