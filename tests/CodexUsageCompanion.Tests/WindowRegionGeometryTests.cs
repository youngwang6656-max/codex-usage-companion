using CodexUsageCompanion.Windows;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class WindowRegionGeometryTests
{
    [Theory]
    [InlineData(50, 26)]
    [InlineData(63, 33)]
    [InlineData(75, 39)]
    [InlineData(100, 52)]
    public void FromPlacement_ScalesThirteenDipCornerWithoutTransparency(
        int physicalHeight,
        int expectedEllipseDiameter)
    {
        var placement = new CompanionPlacement(
            100,
            700,
            1200,
            physicalHeight,
            CompanionLayoutMode.BottomAttached);

        RoundRegionGeometry region = WindowRegionGeometry.FromPlacement(placement);

        Assert.Equal(1201, region.Right);
        Assert.Equal(physicalHeight + 1, region.Bottom);
        Assert.Equal(expectedEllipseDiameter, region.EllipseWidth);
        Assert.Equal(expectedEllipseDiameter, region.EllipseHeight);
    }
}
