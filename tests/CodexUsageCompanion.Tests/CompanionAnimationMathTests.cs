using CodexUsageCompanion.Windows;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class CompanionAnimationMathTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.5, 0.5)]
    [InlineData(1, 1)]
    public void CubicEaseKeepsEndpointsAndMidpoint(double progress, double expected)
    {
        Assert.Equal(expected, CompanionAnimationMath.CubicEaseInOut(progress), 6);
    }

    [Fact]
    public void ReverseDurationStartsFromCurrentVisualProgress()
    {
        TimeSpan duration = CompanionAnimationMath.ScaleDuration(
            TimeSpan.FromMilliseconds(160),
            startProgress: 0.65,
            targetProgress: 0);

        Assert.Equal(TimeSpan.FromMilliseconds(104), duration);
    }

    [Fact]
    public void InterpolateRoundsPhysicalCoordinatesConsistently()
    {
        Assert.Equal(16, CompanionAnimationMath.Interpolate(10, 20, 0.55));
    }
}
