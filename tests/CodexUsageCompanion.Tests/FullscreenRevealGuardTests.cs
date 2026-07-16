using CodexUsageCompanion.Windows;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class FullscreenRevealGuardTests
{
    [Fact]
    public void ThreeConsecutiveSlowFramesFailStrictGate()
    {
        var guard = new FullscreenRevealGuard();

        Assert.Null(guard.ReportFrame(TimeSpan.FromMilliseconds(34), 0));
        Assert.Null(guard.ReportFrame(TimeSpan.FromMilliseconds(40), 0));
        Assert.Equal(
            FullscreenRevealFailureReason.ConsecutiveSlowFrames,
            guard.ReportFrame(TimeSpan.FromMilliseconds(35), 0));
    }

    [Fact]
    public void GeometryDeviationAboveTwoPixelsFails()
    {
        var guard = new FullscreenRevealGuard();

        Assert.Equal(
            FullscreenRevealFailureReason.GeometryDeviation,
            guard.ReportFrame(TimeSpan.FromMilliseconds(16), 3));
    }

    [Fact]
    public void MoreThanQuarterLateFramesFailsAfterMinimumSample()
    {
        var guard = new FullscreenRevealGuard();

        for (int index = 0; index < 5; index++)
        {
            _ = guard.ReportFrame(TimeSpan.FromMilliseconds(16), 0);
        }

        _ = guard.ReportFrame(TimeSpan.FromMilliseconds(34), 0);
        _ = guard.ReportFrame(TimeSpan.FromMilliseconds(16), 0);
        _ = guard.ReportFrame(TimeSpan.FromMilliseconds(34), 0);
        _ = guard.ReportFrame(TimeSpan.FromMilliseconds(16), 0);
        FullscreenRevealFailureReason? reason = guard.ReportFrame(
            TimeSpan.FromMilliseconds(34),
            0);

        Assert.Equal(FullscreenRevealFailureReason.LateFrameRatio, reason);
    }

    [Fact]
    public void TimeoutHasExplicitReason()
    {
        var guard = new FullscreenRevealGuard();

        Assert.Equal(
            FullscreenRevealFailureReason.Timeout,
            guard.CheckElapsed(TimeSpan.FromMilliseconds(351)));
    }

    [Fact]
    public void UserInterruptionHasExplicitReason()
    {
        var guard = new FullscreenRevealGuard();

        Assert.Equal(
            FullscreenRevealFailureReason.UserInterrupted,
            guard.Interrupt());
    }
}
