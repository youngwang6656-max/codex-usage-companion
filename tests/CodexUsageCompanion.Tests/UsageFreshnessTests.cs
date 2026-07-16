using CodexUsageCompanion.Usage;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class UsageFreshnessTests
{
    private static readonly DateTimeOffset UpdatedAt = new(2026, 7, 15, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OnConnectionFailure_KeepsRecentValuesWhileSyncing()
    {
        UsageSnapshot previous = new("Plus", 46, UpdatedAt.AddDays(7), 1, SyncState.Synced, UpdatedAt);

        UsageSnapshot result = UsageFreshness.OnConnectionFailure(previous, UpdatedAt.AddMinutes(4));

        Assert.Equal(SyncState.Syncing, result.State);
        Assert.Equal(46, result.RemainingPercent);
        Assert.Equal(1, result.AvailableResetCount);
    }

    [Fact]
    public void OnConnectionFailure_ClearsValuesAfterFiveMinutes()
    {
        UsageSnapshot previous = new("Plus", 46, UpdatedAt.AddDays(7), 1, SyncState.Synced, UpdatedAt);

        UsageSnapshot result = UsageFreshness.OnConnectionFailure(previous, UpdatedAt.AddMinutes(5));

        Assert.Equal(SyncState.Offline, result.State);
        Assert.Equal("Plus", result.PlanLabel);
        Assert.Null(result.RemainingPercent);
        Assert.Null(result.ResetsAt);
        Assert.Null(result.AvailableResetCount);
    }

    [Fact]
    public void AuthenticationFailure_ClearsAllUsageImmediately()
    {
        UsageSnapshot previous = new("Plus", 46, UpdatedAt.AddDays(7), 1, SyncState.Synced, UpdatedAt);

        UsageSnapshot result = UsageFreshness.AuthenticationRequired(previous, UpdatedAt.AddMinutes(1));

        Assert.Equal(SyncState.AuthenticationRequired, result.State);
        Assert.Null(result.PlanLabel);
        Assert.Null(result.RemainingPercent);
        Assert.Null(result.AvailableResetCount);
    }
}
