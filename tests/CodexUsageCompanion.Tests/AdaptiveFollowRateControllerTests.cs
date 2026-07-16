using CodexUsageCompanion.Windows;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class AdaptiveFollowRateControllerTests
{
    [Fact]
    public void MoveStart_UsesSixtyFps_AndMoveEndReturnsToThirty()
    {
        var controller = new AdaptiveFollowRateController();

        Assert.Equal(30, controller.CurrentFps);

        controller.OnMoveSizeStarted();

        Assert.Equal(60, controller.CurrentFps);
        Assert.InRange(controller.FrameInterval.TotalMilliseconds, 16, 17);

        controller.OnMoveSizeEnded();

        Assert.Equal(30, controller.CurrentFps);
    }

    [Fact]
    public void HighCpu_DowngradesOneLevelPerSample_AndStopsAtFifteen()
    {
        var controller = MovingController();

        controller.Report(cpuPercent: 5.1, missedFrameRatio: 0);
        Assert.Equal(30, controller.CurrentFps);
        controller.Report(cpuPercent: 6, missedFrameRatio: 0);
        Assert.Equal(20, controller.CurrentFps);
        controller.Report(cpuPercent: 7, missedFrameRatio: 0);
        Assert.Equal(15, controller.CurrentFps);
        controller.Report(cpuPercent: 9, missedFrameRatio: 0);
        Assert.Equal(15, controller.CurrentFps);
    }

    [Fact]
    public void MissedFramesAboveTwentyFivePercent_DowngradeOneLevel()
    {
        var controller = MovingController();

        controller.Report(cpuPercent: 1, missedFrameRatio: 0.251);

        Assert.Equal(30, controller.CurrentFps);
    }

    [Fact]
    public void TwoStableSamplesRecoverOneLevel_WithoutExceedingSixty()
    {
        var controller = MovingController();
        controller.Report(6, 0);
        controller.Report(6, 0);
        controller.Report(6, 0);
        Assert.Equal(15, controller.CurrentFps);

        for (int expected = 20; expected <= 60; expected = expected == 20 ? 30 : 60)
        {
            controller.Report(1.5, 0.01);
            Assert.NotEqual(expected, controller.CurrentFps);
            controller.Report(1.5, 0.01);
            Assert.Equal(expected, controller.CurrentFps);
            if (expected == 60)
            {
                break;
            }
        }

        controller.Report(1, 0);
        controller.Report(1, 0);
        Assert.Equal(60, controller.CurrentFps);
    }

    [Fact]
    public void UnstableSampleResetsRecoveryStreak()
    {
        var controller = MovingController();
        controller.Report(6, 0);
        Assert.Equal(30, controller.CurrentFps);

        controller.Report(1, 0);
        controller.Report(3, 0);
        controller.Report(1, 0);
        Assert.Equal(30, controller.CurrentFps);
        controller.Report(1, 0);
        Assert.Equal(60, controller.CurrentFps);
    }

    private static AdaptiveFollowRateController MovingController()
    {
        var controller = new AdaptiveFollowRateController();
        controller.OnMoveSizeStarted();
        return controller;
    }
}
