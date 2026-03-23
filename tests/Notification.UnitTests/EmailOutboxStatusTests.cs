using Notification.Domain;

namespace Notification.UnitTests;

public sealed class EmailOutboxStatusTests
{
    [Fact]
    public void EmailOutboxStatus_HasExpectedValues()
    {
        Assert.Equal(0, (int)EmailOutboxStatus.Pending);
        Assert.Equal(1, (int)EmailOutboxStatus.Processing);
        Assert.Equal(2, (int)EmailOutboxStatus.Sent);
        Assert.Equal(3, (int)EmailOutboxStatus.Failed);
        Assert.Equal(4, (int)EmailOutboxStatus.Cancelled);
    }

    [Fact]
    public void EmailOutboxStatus_HasExactlyFiveValues()
    {
        var values = Enum.GetValues<EmailOutboxStatus>();
        Assert.Equal(5, values.Length);
    }

    [Fact]
    public void EmailOutboxStatus_DefaultIsPending()
    {
        var status = default(EmailOutboxStatus);
        Assert.Equal(EmailOutboxStatus.Pending, status);
    }
}
