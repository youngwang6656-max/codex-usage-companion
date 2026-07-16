namespace CodexUsageCompanion.Usage;

public enum SyncState
{
    Syncing,
    Synced,
    Offline,
    AuthenticationRequired,
    Disabled,
}

public sealed record UsageSnapshot(
    string? PlanLabel,
    int? RemainingPercent,
    DateTimeOffset? ResetsAt,
    int? AvailableResetCount,
    SyncState State,
    DateTimeOffset UpdatedAt);

public sealed record RateLimitWindow(
    double UsedPercent,
    long? ResetsAtUnixSeconds,
    int? WindowDurationMinutes);
