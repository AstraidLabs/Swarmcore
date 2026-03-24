using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using BeeTracker.BuildingBlocks.Abstractions.Options;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Infrastructure;

namespace Tracker.Gateway.UnitTests;

public sealed class AnnounceRequestValidatorTests
{
    [Fact]
    public void Validate_ValidRequest_ReturnsSuccess()
    {
        var validator = CreateValidator();
        var request = CreateRequest(numwant: 50);

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyEndpoint_ReturnsError()
    {
        var validator = CreateValidator();
        var request = new AnnounceRequest(
            InfoHashKey.FromBytes(new byte[20]),
            PeerIdKey.FromBytes(new byte[20]),
            default,
            0, 0, 100, 50, true, false, TrackerEvent.Started, null, null, null);

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains("endpoint", result.Error.FailureReason);
    }

    [Fact]
    public void Validate_NegativeUploaded_ReturnsError()
    {
        var validator = CreateValidator();
        var request = new AnnounceRequest(
            InfoHashKey.FromBytes(new byte[20]),
            PeerIdKey.FromBytes(new byte[20]),
            PeerEndpoint.FromIPv4(0x7F000001, 6881),
            -1, 0, 100, 50, true, false, TrackerEvent.Started, null, null, null);

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains("negative", result.Error.FailureReason);
    }

    [Fact]
    public void Validate_NegativeDownloaded_ReturnsError()
    {
        var validator = CreateValidator();
        var request = new AnnounceRequest(
            InfoHashKey.FromBytes(new byte[20]),
            PeerIdKey.FromBytes(new byte[20]),
            PeerEndpoint.FromIPv4(0x7F000001, 6881),
            0, -1, 100, 50, true, false, TrackerEvent.Started, null, null, null);

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_NegativeLeft_ReturnsError()
    {
        var validator = CreateValidator();
        var request = new AnnounceRequest(
            InfoHashKey.FromBytes(new byte[20]),
            PeerIdKey.FromBytes(new byte[20]),
            PeerEndpoint.FromIPv4(0x7F000001, 6881),
            0, 0, -1, 50, true, false, TrackerEvent.Started, null, null, null);

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_NumWantExceedsHardMax_ReturnsError()
    {
        var validator = CreateValidator(hardMaxNumWant: 100);
        var request = CreateRequest(numwant: 101);

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains("numwant", result.Error.FailureReason);
    }

    [Fact]
    public void Validate_NumWantAtHardMax_ReturnsSuccess()
    {
        var validator = CreateValidator(hardMaxNumWant: 100);
        var request = CreateRequest(numwant: 100);

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_NumWantZero_ReturnsSuccess()
    {
        var validator = CreateValidator();
        var request = CreateRequest(numwant: 0);

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_NumWantMinusOne_ReturnsSuccess()
    {
        var validator = CreateValidator();
        var request = CreateRequest(numwant: -1);

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_NumWantMinusTwo_ReturnsError()
    {
        var validator = CreateValidator();
        var request = CreateRequest(numwant: -2);

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_NonCompactWhenRequired_ReturnsError()
    {
        var validator = CreateValidator(requireCompact: true);
        var request = new AnnounceRequest(
            InfoHashKey.FromBytes(new byte[20]),
            PeerIdKey.FromBytes(new byte[20]),
            PeerEndpoint.FromIPv4(0x7F000001, 6881),
            0, 0, 100, 50, false, false, TrackerEvent.Started, null, null, null);

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains("compact", result.Error.FailureReason);
    }

    [Fact]
    public void Validate_NonCompactWhenNotRequired_ReturnsSuccess()
    {
        var validator = CreateValidator(requireCompact: false);
        var request = new AnnounceRequest(
            InfoHashKey.FromBytes(new byte[20]),
            PeerIdKey.FromBytes(new byte[20]),
            PeerEndpoint.FromIPv4(0x7F000001, 6881),
            0, 0, 100, 50, false, false, TrackerEvent.Started, null, null, null);

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    private static AnnounceRequestValidator CreateValidator(int hardMaxNumWant = 200, bool requireCompact = false)
    {
        return new AnnounceRequestValidator(
            Options.Create(new TrackerSecurityOptions
            {
                HardMaxNumWant = hardMaxNumWant,
                RequireCompactResponses = requireCompact
            }),
            new RuntimeGovernanceStateService(
                Options.Create(new TrackerGovernanceOptions()),
                Options.Create(new TrackerCompatibilityOptions())));
    }

    private static AnnounceRequest CreateRequest(int numwant)
    {
        return new AnnounceRequest(
            InfoHashKey.FromBytes(new byte[20]),
            PeerIdKey.FromBytes(new byte[20]),
            PeerEndpoint.FromIPv4(0x7F000001, 6881),
            0, 0, 100, numwant, true, false, TrackerEvent.Started, null, null, null);
    }
}
