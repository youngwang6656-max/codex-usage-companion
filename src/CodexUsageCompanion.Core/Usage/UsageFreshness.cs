namespace CodexUsageCompanion.Usage;

public static class UsageFreshness
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    public static UsageSnapshot OnConnectionFailure(UsageSnapshot previous, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(previous);

        if (now - previous.UpdatedAt < StaleAfter)
        {
            return previous with { State = SyncState.Syncing };
        }

        return previous with
        {
            RemainingPercent = null,
            ResetsAt = null,
            AvailableResetCount = null,
            State = SyncState.Offline,
        };
    }

    public static UsageSnapshot AuthenticationRequired(UsageSnapshot previous, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(previous);

        return new UsageSnapshot(
            PlanLabel: null,
            RemainingPercent: null,
            ResetsAt: null,
            AvailableResetCount: null,
            State: SyncState.AuthenticationRequired,
            UpdatedAt: now);
    }
}
