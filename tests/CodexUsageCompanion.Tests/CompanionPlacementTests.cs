using CodexUsageCompanion.Windows;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class CompanionPlacementTests
{
    [Theory]
    [InlineData(96, 50)]
    [InlineData(120, 63)]
    [InlineData(144, 75)]
    [InlineData(192, 100)]
    public void Calculate_ScalesHeightAndLeavesNoGap(uint dpi, int expectedHeight)
    {
        var state = new CodexWindowState(
            (nint)42,
            new PixelRect(100, 100, 1100, 700),
            dpi,
            IsVisible: true,
            IsMinimized: false,
            @"C:\Program Files\WindowsApps\OpenAI.Codex_1\app\ChatGPT.exe");

        CompanionPlacement result = CompanionPlacementCalculator.Calculate(state);

        Assert.Equal(expectedHeight, result.Height);
        Assert.Equal(698, result.Top);
        Assert.Equal(1000, result.Width);
        Assert.False(result.PlacedInsideCodex);
    }

    [Fact]
    public void Calculate_DoesNotClampWhenBelowWouldLeaveWorkArea()
    {
        var state = new CodexWindowState(
            (nint)42,
            new PixelRect(50, 400, 1250, 1040),
            Dpi: 96,
            IsVisible: true,
            IsMinimized: false,
            @"C:\Program Files\WindowsApps\OpenAI.Codex_1\app\ChatGPT.exe");

        CompanionPlacement result = CompanionPlacementCalculator.Calculate(state);

        Assert.Equal(1038, result.Top);
        Assert.False(result.PlacedInsideCodex);
    }

    [Fact]
    public void Calculate_DoesNotSpecialCaseFullscreenCodex()
    {
        var state = new CodexWindowState(
            (nint)42,
            new PixelRect(0, 0, 1920, 1080),
            Dpi: 96,
            IsVisible: true,
            IsMinimized: false,
            @"C:\Program Files\WindowsApps\OpenAI.Codex_1\app\ChatGPT.exe");

        CompanionPlacement result = CompanionPlacementCalculator.Calculate(state);

        Assert.Equal(1078, result.Top);
        Assert.Equal(1920, result.Width);
        Assert.False(result.PlacedInsideCodex);
    }
}
