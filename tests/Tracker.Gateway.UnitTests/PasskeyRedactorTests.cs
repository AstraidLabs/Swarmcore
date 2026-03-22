using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Infrastructure;

namespace Tracker.Gateway.UnitTests;

public sealed class PasskeyRedactorTests
{
    private readonly IPasskeyRedactor _redactor = new TrackerPasskeyRedactor();

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("abcdef", "***")]
    [InlineData("bootstrap-passkey", "boo***key")]
    public void Redact_MasksExpectedValues(string? passkey, string? expected)
    {
        var redacted = _redactor.Redact(passkey);
        Assert.Equal(expected, redacted);
    }
}
