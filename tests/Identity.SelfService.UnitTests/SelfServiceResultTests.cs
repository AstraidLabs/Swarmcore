using Identity.SelfService.Application;

namespace Identity.SelfService.UnitTests;

public sealed class SelfServiceResultTests
{
    [Fact]
    public void Ok_WithUserId_ReturnsSuccessResult()
    {
        var result = SelfServiceResult.Ok("user-123");

        Assert.True(result.Success);
        Assert.Equal("user-123", result.UserId);
        Assert.Null(result.ErrorCode);
        Assert.Empty(result.ErrorMessages);
    }

    [Fact]
    public void Ok_WithNullUserId_ReturnsSuccessResult()
    {
        var result = SelfServiceResult.Ok(null);

        Assert.True(result.Success);
        Assert.Null(result.UserId);
    }

    [Fact]
    public void Fail_WithCodeAndMessage_ReturnsFailureResult()
    {
        var result = SelfServiceResult.Fail("VALIDATION_ERROR", "Password too short");

        Assert.False(result.Success);
        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
        Assert.Single(result.ErrorMessages);
        Assert.Equal("Password too short", result.ErrorMessages[0]);
        Assert.Null(result.UserId);
    }

    [Fact]
    public void Fail_WithCodeAndMultipleMessages_ReturnsFailureResult()
    {
        var messages = new List<string> { "Error 1", "Error 2", "Error 3" };
        var result = SelfServiceResult.Fail("MULTI_ERROR", messages);

        Assert.False(result.Success);
        Assert.Equal("MULTI_ERROR", result.ErrorCode);
        Assert.Equal(3, result.ErrorMessages.Count);
        Assert.Equal("Error 1", result.ErrorMessages[0]);
        Assert.Equal("Error 2", result.ErrorMessages[1]);
        Assert.Equal("Error 3", result.ErrorMessages[2]);
    }

    [Fact]
    public void Fail_NeverReturnsUserId()
    {
        var result = SelfServiceResult.Fail("ERROR", "Something went wrong");
        Assert.Null(result.UserId);
    }
}
