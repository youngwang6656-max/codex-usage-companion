using CodexUsageCompanion.Windows;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class WaitableTimerWindowFrameClockTests
{
    [Fact]
    public void ForcedFallbackUsesManagedTimerClock()
    {
        using var clock = new WaitableTimerWindowFrameClock(forceFallback: true);

        Assert.False(clock.IsHighResolution);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScheduledOneShotRaisesTickInHighResolutionOrFallbackMode(bool forceFallback)
    {
        using var clock = new WaitableTimerWindowFrameClock(forceFallback);
        using var ticked = new ManualResetEventSlim();
        clock.Tick += ticked.Set;

        clock.Schedule(TimeSpan.FromMilliseconds(5));

        Assert.True(ticked.Wait(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken));
    }
}
