using CodexUsageCompanion.Windows;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class AnchorCalibrationPlannerTests
{
    [Fact]
    public void IgnoresCoordinateDifferenceWithinTwoPixels()
    {
        AnchorCalibrationPlan plan = AnchorCalibrationPlanner.Create(
            new PixelRect(100, 200, 128, 228),
            new PixelRect(102, 199, 130, 227),
            isMoving: false);

        Assert.Equal(AnchorCalibrationAction.None, plan.Action);
        Assert.Equal(TimeSpan.Zero, plan.Duration);
    }

    [Fact]
    public void DefersLargeCorrectionWhileWindowIsMoving()
    {
        AnchorCalibrationPlan plan = AnchorCalibrationPlanner.Create(
            new PixelRect(100, 200, 128, 228),
            new PixelRect(150, 210, 178, 238),
            isMoving: true);

        Assert.Equal(AnchorCalibrationAction.Defer, plan.Action);
    }

    [Fact]
    public void AnimatesLargeStationaryCorrectionForOneHundredMilliseconds()
    {
        AnchorCalibrationPlan plan = AnchorCalibrationPlanner.Create(
            new PixelRect(100, 200, 128, 228),
            new PixelRect(150, 210, 178, 238),
            isMoving: false);

        Assert.Equal(AnchorCalibrationAction.Animate, plan.Action);
        Assert.Equal(TimeSpan.FromMilliseconds(100), plan.Duration);
    }
}
