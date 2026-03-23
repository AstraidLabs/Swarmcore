namespace Identity.SelfService.Domain;

// ─── Account State (Domain-level) ───────────────────────────────────────────

public enum AdminAccountState
{
    PendingActivation = 0,
    Active = 1,
    Suspended = 2,
    Deactivated = 3,
    ReactivationPending = 4,
    PasswordResetPending = 5,
    Locked = 6,
}

// ─── State Transition Rules ─────────────────────────────────────────────────

public static class AccountStateTransition
{
    private static readonly IReadOnlyDictionary<AdminAccountState, IReadOnlySet<AdminAccountState>> AllowedTransitions =
        new Dictionary<AdminAccountState, IReadOnlySet<AdminAccountState>>
        {
            [AdminAccountState.PendingActivation] = new HashSet<AdminAccountState>
            {
                AdminAccountState.Active,
                AdminAccountState.Locked,
            },
            [AdminAccountState.Active] = new HashSet<AdminAccountState>
            {
                AdminAccountState.Suspended,
                AdminAccountState.Deactivated,
                AdminAccountState.PasswordResetPending,
                AdminAccountState.Locked,
            },
            [AdminAccountState.Suspended] = new HashSet<AdminAccountState>
            {
                AdminAccountState.Active,
                AdminAccountState.Deactivated,
            },
            [AdminAccountState.Deactivated] = new HashSet<AdminAccountState>
            {
                AdminAccountState.ReactivationPending,
            },
            [AdminAccountState.ReactivationPending] = new HashSet<AdminAccountState>
            {
                AdminAccountState.Active,
                AdminAccountState.Deactivated,
            },
            [AdminAccountState.PasswordResetPending] = new HashSet<AdminAccountState>
            {
                AdminAccountState.Active,
            },
            [AdminAccountState.Locked] = new HashSet<AdminAccountState>
            {
                AdminAccountState.Active,
                AdminAccountState.Suspended,
            },
        };

    public static bool CanTransition(AdminAccountState from, AdminAccountState to)
    {
        return AllowedTransitions.TryGetValue(from, out var targets) && targets.Contains(to);
    }
}

// ─── Exception ──────────────────────────────────────────────────────────────

public sealed class InvalidAccountStateTransitionException : Exception
{
    public InvalidAccountStateTransitionException(AdminAccountState from, AdminAccountState to)
        : base($"Invalid account state transition from '{from}' to '{to}'.")
    {
        From = from;
        To = to;
    }

    public InvalidAccountStateTransitionException(AdminAccountState from, AdminAccountState to, string message)
        : base(message)
    {
        From = from;
        To = to;
    }

    public InvalidAccountStateTransitionException(AdminAccountState from, AdminAccountState to, string message, Exception innerException)
        : base(message, innerException)
    {
        From = from;
        To = to;
    }

    public AdminAccountState From { get; }
    public AdminAccountState To { get; }
}
