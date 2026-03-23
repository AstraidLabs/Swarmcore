using Identity.SelfService.Domain;

namespace Identity.SelfService.UnitTests;

public sealed class AccountStateTransitionTests
{
    // ─── Valid transitions ─────────────────────────────────────────────────

    [Theory]
    [InlineData(AdminAccountState.PendingActivation, AdminAccountState.Active)]
    [InlineData(AdminAccountState.PendingActivation, AdminAccountState.Locked)]
    [InlineData(AdminAccountState.Active, AdminAccountState.Suspended)]
    [InlineData(AdminAccountState.Active, AdminAccountState.Deactivated)]
    [InlineData(AdminAccountState.Active, AdminAccountState.PasswordResetPending)]
    [InlineData(AdminAccountState.Active, AdminAccountState.Locked)]
    [InlineData(AdminAccountState.Suspended, AdminAccountState.Active)]
    [InlineData(AdminAccountState.Suspended, AdminAccountState.Deactivated)]
    [InlineData(AdminAccountState.Deactivated, AdminAccountState.ReactivationPending)]
    [InlineData(AdminAccountState.ReactivationPending, AdminAccountState.Active)]
    [InlineData(AdminAccountState.ReactivationPending, AdminAccountState.Deactivated)]
    [InlineData(AdminAccountState.PasswordResetPending, AdminAccountState.Active)]
    [InlineData(AdminAccountState.Locked, AdminAccountState.Active)]
    [InlineData(AdminAccountState.Locked, AdminAccountState.Suspended)]
    public void CanTransition_AllowedPaths_ReturnsTrue(AdminAccountState from, AdminAccountState to)
    {
        Assert.True(AccountStateTransition.CanTransition(from, to));
    }

    // ─── Invalid transitions ───────────────────────────────────────────────

    [Theory]
    [InlineData(AdminAccountState.PendingActivation, AdminAccountState.Deactivated)]
    [InlineData(AdminAccountState.PendingActivation, AdminAccountState.Suspended)]
    [InlineData(AdminAccountState.PendingActivation, AdminAccountState.PasswordResetPending)]
    [InlineData(AdminAccountState.Active, AdminAccountState.PendingActivation)]
    [InlineData(AdminAccountState.Active, AdminAccountState.ReactivationPending)]
    [InlineData(AdminAccountState.Suspended, AdminAccountState.PendingActivation)]
    [InlineData(AdminAccountState.Suspended, AdminAccountState.Locked)]
    [InlineData(AdminAccountState.Suspended, AdminAccountState.PasswordResetPending)]
    [InlineData(AdminAccountState.Deactivated, AdminAccountState.Active)]
    [InlineData(AdminAccountState.Deactivated, AdminAccountState.PendingActivation)]
    [InlineData(AdminAccountState.Deactivated, AdminAccountState.Locked)]
    [InlineData(AdminAccountState.ReactivationPending, AdminAccountState.Suspended)]
    [InlineData(AdminAccountState.ReactivationPending, AdminAccountState.Locked)]
    [InlineData(AdminAccountState.PasswordResetPending, AdminAccountState.Deactivated)]
    [InlineData(AdminAccountState.PasswordResetPending, AdminAccountState.Locked)]
    [InlineData(AdminAccountState.Locked, AdminAccountState.Deactivated)]
    [InlineData(AdminAccountState.Locked, AdminAccountState.PendingActivation)]
    public void CanTransition_ForbiddenPaths_ReturnsFalse(AdminAccountState from, AdminAccountState to)
    {
        Assert.False(AccountStateTransition.CanTransition(from, to));
    }

    // ─── Self-transitions ──────────────────────────────────────────────────

    [Theory]
    [InlineData(AdminAccountState.PendingActivation)]
    [InlineData(AdminAccountState.Active)]
    [InlineData(AdminAccountState.Suspended)]
    [InlineData(AdminAccountState.Deactivated)]
    [InlineData(AdminAccountState.ReactivationPending)]
    [InlineData(AdminAccountState.PasswordResetPending)]
    [InlineData(AdminAccountState.Locked)]
    public void CanTransition_SameState_ReturnsFalse(AdminAccountState state)
    {
        Assert.False(AccountStateTransition.CanTransition(state, state));
    }

    // ─── Exception ─────────────────────────────────────────────────────────

    [Fact]
    public void InvalidAccountStateTransitionException_CapturesFromAndTo()
    {
        var ex = new InvalidAccountStateTransitionException(
            AdminAccountState.Deactivated, AdminAccountState.Active);

        Assert.Equal(AdminAccountState.Deactivated, ex.From);
        Assert.Equal(AdminAccountState.Active, ex.To);
        Assert.Contains("Deactivated", ex.Message);
        Assert.Contains("Active", ex.Message);
    }

    [Fact]
    public void InvalidAccountStateTransitionException_CustomMessage()
    {
        var ex = new InvalidAccountStateTransitionException(
            AdminAccountState.Locked, AdminAccountState.PendingActivation, "Custom message");

        Assert.Equal("Custom message", ex.Message);
        Assert.Equal(AdminAccountState.Locked, ex.From);
        Assert.Equal(AdminAccountState.PendingActivation, ex.To);
    }

    [Fact]
    public void InvalidAccountStateTransitionException_WithInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new InvalidAccountStateTransitionException(
            AdminAccountState.Active, AdminAccountState.PendingActivation, "outer", inner);

        Assert.Same(inner, ex.InnerException);
        Assert.Equal("outer", ex.Message);
    }

    // ─── Enum coverage ─────────────────────────────────────────────────────

    [Fact]
    public void AdminAccountState_HasExpectedValues()
    {
        Assert.Equal(0, (int)AdminAccountState.PendingActivation);
        Assert.Equal(1, (int)AdminAccountState.Active);
        Assert.Equal(2, (int)AdminAccountState.Suspended);
        Assert.Equal(3, (int)AdminAccountState.Deactivated);
        Assert.Equal(4, (int)AdminAccountState.ReactivationPending);
        Assert.Equal(5, (int)AdminAccountState.PasswordResetPending);
        Assert.Equal(6, (int)AdminAccountState.Locked);
    }

    [Fact]
    public void AllStates_HaveAtLeastOneAllowedTransition()
    {
        foreach (var state in Enum.GetValues<AdminAccountState>())
        {
            var hasTransition = Enum.GetValues<AdminAccountState>()
                .Any(target => AccountStateTransition.CanTransition(state, target));
            Assert.True(hasTransition, $"State {state} has no allowed transitions");
        }
    }
}
