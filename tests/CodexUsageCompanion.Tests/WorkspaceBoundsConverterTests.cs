using CodexUsageCompanion.Windows;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class WorkspaceBoundsConverterTests
{
    [Fact]
    public void ToScreen_AddsTopTaskbarWorkspaceOffset()
    {
        PixelRect result = WorkspaceBoundsConverter.ToScreen(
            new PixelRect(240, 160, 1440, 900),
            monitorBounds: new PixelRect(0, 0, 1920, 1080),
            workArea: new PixelRect(0, 48, 1920, 1080));

        Assert.Equal(new PixelRect(240, 208, 1440, 948), result);
    }

    [Fact]
    public void ToScreen_AddsLeftTaskbarWorkspaceOffsetOnSecondaryMonitor()
    {
        PixelRect result = WorkspaceBoundsConverter.ToScreen(
            new PixelRect(2_100, 120, 3_300, 860),
            monitorBounds: new PixelRect(1_920, 0, 3_840, 1080),
            workArea: new PixelRect(1_984, 0, 3_840, 1080));

        Assert.Equal(new PixelRect(2_164, 120, 3_364, 860), result);
    }
}
