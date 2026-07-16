using CodexUsageCompanion.Windows;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class CompanionRevealFrameTests
{
    [Theory]
    [InlineData(96u, 1254, 911, 1282, 939)]
    [InlineData(144u, 1231, 904, 1273, 946)]
    public void ExpandedPowerButtonUsesTwentyEightDipHitTargetAndEighteenDipRightInset(
        uint dpi,
        int expectedLeft,
        int expectedTop,
        int expectedRight,
        int expectedBottom)
    {
        var tray = new CompanionPlacement(
            100,
            900,
            1200,
            50,
            CompanionLayoutMode.BottomAttached);

        PixelRect bounds = CompanionButtonPlacement.GetExpandedPowerButton(tray, dpi);

        Assert.Equal(
            new PixelRect(expectedLeft, expectedTop, expectedRight, expectedBottom),
            bounds);
    }

    [Fact]
    public void RevealFrameMovesButtonAndGrowsTrayFromCodexEdge()
    {
        var tray = new CompanionPlacement(
            100,
            900,
            1200,
            50,
            CompanionLayoutMode.BottomAttached);
        var collapsed = new ComposerAnchor(
            new PixelRect(1112, 802, 1140, 830),
            0,
            ComposerAnchorSource.FixedFallback);

        CompanionRevealFrame start = CompanionRevealFrameCalculator.Calculate(
            tray,
            collapsed,
            visualProgress: 0,
            dpi: 96);
        CompanionRevealFrame middle = CompanionRevealFrameCalculator.Calculate(
            tray,
            collapsed,
            visualProgress: 0.5,
            dpi: 96);
        CompanionRevealFrame end = CompanionRevealFrameCalculator.Calculate(
            tray,
            collapsed,
            visualProgress: 1,
            dpi: 96);

        Assert.Equal(0, start.VisibleTrayHeight);
        Assert.Equal(collapsed.ButtonBounds, start.ToggleBounds);
        Assert.Equal(25, middle.VisibleTrayHeight);
        Assert.Equal(new PixelRect(1183, 857, 1211, 885), middle.ToggleBounds);
        Assert.Equal(50, end.VisibleTrayHeight);
        Assert.Equal(
            CompanionButtonPlacement.GetExpandedPowerButton(tray, 96),
            end.ToggleBounds);
    }

    [Fact]
    public void RefreshButtonIsLeftOfPowerWithFourDipGap()
    {
        var tray = new CompanionPlacement(
            100,
            900,
            1200,
            50,
            CompanionLayoutMode.BottomAttached);

        PixelRect power = CompanionButtonPlacement.GetExpandedPowerButton(tray, 96);
        PixelRect refresh = CompanionButtonPlacement.GetExpandedRefreshButton(tray, 96);

        Assert.Equal(4, power.Left - refresh.Right);
        Assert.Equal(28, refresh.Width);
    }
}
