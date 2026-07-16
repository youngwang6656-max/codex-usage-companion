using CodexUsageCompanion.Protocol;
using CodexUsageCompanion.Usage;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class CodexUsageClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StartAsync_InitializesSessionAndPublishesCombinedSnapshot()
    {
        var session = new FakeSession();
        await using var client = new CodexUsageClient(session, new FixedClock(Now), TimeSpan.FromHours(1));
        UsageSnapshot? observed = null;
        client.SnapshotChanged += (_, snapshot) => observed = snapshot;

        await client.StartAsync(CancellationToken.None);

        Assert.True(session.Started);
        Assert.Equal(1, session.AccountReads);
        Assert.Equal(1, session.RateLimitReads);
        Assert.NotNull(observed);
        Assert.Equal("Plus", observed.PlanLabel);
        Assert.Equal(46, observed.RemainingPercent);
        Assert.Equal(1, observed.AvailableResetCount);
        Assert.Equal(SyncState.Synced, observed.State);
    }

    [Fact]
    public async Task RateLimitNotification_UpdatesSnapshotWithoutRefetching()
    {
        var session = new FakeSession();
        await using var client = new CodexUsageClient(session, new FixedClock(Now), TimeSpan.FromHours(1));
        await client.StartAsync(CancellationToken.None);
        var refreshed = new TaskCompletionSource<UsageSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.RemainingPercent == 40)
            {
                refreshed.TrySetResult(snapshot);
            }
        };

        session.NotifyRateLimitsUpdated(FakeSession.RateLimitsNotificationJsonFor(usedPercent: 60));
        UsageSnapshot result = await refreshed.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, session.AccountReads);
        Assert.Equal(1, session.RateLimitReads);
        Assert.Equal("Plus", result.PlanLabel);
        Assert.Equal(40, result.RemainingPercent);
        Assert.Equal(1, result.AvailableResetCount);
    }

    [Fact]
    public async Task StartAsync_PublishesAuthenticationRequiredWhenAccountIsMissing()
    {
        var session = new FakeSession { AccountJson = "{\"account\":null}" };
        await using var client = new CodexUsageClient(session, new FixedClock(Now), TimeSpan.FromHours(1));
        UsageSnapshot? observed = null;
        client.SnapshotChanged += (_, snapshot) => observed = snapshot;

        await client.StartAsync(CancellationToken.None);

        Assert.NotNull(observed);
        Assert.Equal(SyncState.AuthenticationRequired, observed.State);
        Assert.Equal(0, session.RateLimitReads);
    }

    [Fact]
    public async Task RateLimitPlanType_TakesPriorityOverStaleAccountPlan()
    {
        var session = new FakeSession
        {
            AccountJson = "{\"account\":{\"type\":\"chatgpt\",\"planType\":\"free\"}}",
            RateLimitsJson = FakeSession.RateLimitsJsonFor(54, planType: "plus"),
        };
        await using var client = new CodexUsageClient(session, new FixedClock(Now), TimeSpan.FromHours(1));

        await client.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Plus", client.CurrentSnapshot.PlanLabel);
    }

    [Fact]
    public async Task AccountUpdated_ImmediatelyReplacesConfirmedStalePlan()
    {
        var session = new FakeSession();
        await using var client = new CodexUsageClient(session, new FixedClock(Now), TimeSpan.FromHours(1));
        await client.StartAsync(TestContext.Current.CancellationToken);

        session.NotifyAccountUpdated("pro");

        Assert.Equal("Pro", client.CurrentSnapshot.PlanLabel);
    }

    [Fact]
    public async Task AccountUpdatedWithNoPlan_ClearsStaleDataAsAuthenticationRequired()
    {
        var session = new FakeSession();
        await using var client = new CodexUsageClient(session, new FixedClock(Now), TimeSpan.FromHours(1));
        await client.StartAsync(TestContext.Current.CancellationToken);

        session.NotifyAccountUpdated(null);

        Assert.Equal(SyncState.AuthenticationRequired, client.CurrentSnapshot.State);
        Assert.Null(client.CurrentSnapshot.PlanLabel);
        Assert.Null(client.CurrentSnapshot.RemainingPercent);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; } = now;
    }

    private sealed class FakeSession : ICodexAppServerSession
    {
        public event EventHandler<string>? RateLimitsUpdated;

        public event EventHandler<string?>? AccountUpdated;

        public bool Started { get; private set; }

        public int AccountReads { get; private set; }

        public int RateLimitReads { get; private set; }

        public string AccountJson { get; set; } = "{\"account\":{\"type\":\"chatgpt\",\"planType\":\"plus\"}}";

        public string RateLimitsJson { get; set; } = RateLimitsJsonFor(54);

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started = true;
            return Task.CompletedTask;
        }

        public Task<string> ReadAccountAsync(CancellationToken cancellationToken)
        {
            AccountReads++;
            return Task.FromResult(AccountJson);
        }

        public Task<string> ReadRateLimitsAsync(CancellationToken cancellationToken)
        {
            RateLimitReads++;
            return Task.FromResult(RateLimitsJson);
        }

        public void NotifyRateLimitsUpdated(string notificationJson) =>
            RateLimitsUpdated?.Invoke(this, notificationJson);

        public void NotifyAccountUpdated(string? planType) => AccountUpdated?.Invoke(this, planType);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public static string RateLimitsJsonFor(double usedPercent, string? planType = null) => $$"""
            {
              "rateLimits": {
                "planType": {{(planType is null ? "null" : $"\"{planType}\"")}},
                "primary": null,
                "secondary": {
                  "usedPercent": {{usedPercent}},
                  "resetsAt": {{Now.AddDays(7).ToUnixTimeSeconds()}},
                  "windowDurationMins": 10080
                }
              },
              "rateLimitResetCredits": {"availableCount": 1}
            }
            """;

        public static string RateLimitsNotificationJsonFor(
            double usedPercent,
            string? planType = null) => $$"""
            {
              "rateLimits": {
                "planType": {{(planType is null ? "null" : $"\"{planType}\"")}},
                "primary": null,
                "secondary": {
                  "usedPercent": {{usedPercent}},
                  "resetsAt": {{Now.AddDays(7).ToUnixTimeSeconds()}},
                  "windowDurationMins": 10080
                }
              }
            }
            """;
    }
}
