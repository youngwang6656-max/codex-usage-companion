using CodexUsageCompanion.Windows;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class WindowEventSubscriptionPlannerTests
{
    [Fact]
    public void Startup_UsesOnlyLowFrequencyGlobalForegroundHook()
    {
        IReadOnlyList<WindowEventSubscription> subscriptions =
            WindowEventSubscriptionPlanner.ForStartup();

        WindowEventSubscription subscription = Assert.Single(subscriptions);
        Assert.Equal(0x0003u, subscription.EventMin);
        Assert.Equal(0x0003u, subscription.EventMax);
        Assert.Equal(0u, subscription.ProcessId);
    }

    [Fact]
    public void ForCodexProcess_ScopesMovementAndMinimizeHooksToCodexPid()
    {
        IReadOnlyList<WindowEventSubscription> subscriptions =
            WindowEventSubscriptionPlanner.ForCodexProcess(processId: 30328);

        Assert.Collection(
            subscriptions,
            subscription =>
            {
                Assert.Equal(0x000Au, subscription.EventMin);
                Assert.Equal(0x000Bu, subscription.EventMax);
                Assert.Equal(30328u, subscription.ProcessId);
            },
            subscription =>
            {
                Assert.Equal(0x0016u, subscription.EventMin);
                Assert.Equal(0x0017u, subscription.EventMax);
                Assert.Equal(30328u, subscription.ProcessId);
            },
            subscription =>
            {
                Assert.Equal(0x8001u, subscription.EventMin);
                Assert.Equal(0x800Bu, subscription.EventMax);
                Assert.Equal(30328u, subscription.ProcessId);
            });
    }
}
