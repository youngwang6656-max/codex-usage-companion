using CodexUsageCompanion.Windows;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class CompanionLayoutPlannerTests
{
    [Fact]
    public void CreateIntegrated_UsesCompanionAsCombinedWorkAreaBottom()
    {
        PixelRect workArea = new(0, 0, 1920, 1080);

        CompanionLayoutDecision result = CompanionLayoutPlanner.CreateIntegrated(
            workArea,
            dpi: 96,
            CompanionLayoutMode.NativeIntegratedFullscreen);

        Assert.Equal(new PixelRect(0, 0, 1920, 1032), result.CodexTarget);
        Assert.Equal(0, result.Companion.Left);
        Assert.Equal(1030, result.Companion.Top);
        Assert.Equal(1920, result.Companion.Width);
        Assert.Equal(50, result.Companion.Height);
        Assert.Equal(1080, result.Companion.Top + result.Companion.Height);
        Assert.Equal(CompanionLayoutMode.NativeIntegratedFullscreen, result.Companion.Mode);
    }

    [Fact]
    public void CreateIntegrated_ScalesBarHeightButKeepsTwoPhysicalPixelOverlap()
    {
        PixelRect workArea = new(0, 0, 2880, 1620);

        CompanionLayoutDecision result = CompanionLayoutPlanner.CreateIntegrated(
            workArea,
            dpi: 144,
            CompanionLayoutMode.ManagedIntegratedFullscreen);

        Assert.Equal(1547, result.CodexTarget!.Value.Bottom);
        Assert.Equal(1545, result.Companion.Top);
        Assert.Equal(75, result.Companion.Height);
        Assert.Equal(1620, result.Companion.Top + result.Companion.Height);
    }

    [Fact]
    public void ForNormalWindow_UsesBottomWhenSpaceIsAvailable()
    {
        CodexWindowState state = State(new PixelRect(100, 100, 1100, 700));

        CompanionLayoutDecision result = CompanionLayoutPlanner.ForNormalWindow(state);

        Assert.Equal(CompanionLayoutMode.BottomAttached, result.Companion.Mode);
        Assert.Equal(698, result.Companion.Top);
        Assert.Null(result.CodexTarget);
    }

    [Fact]
    public void ForNormalWindow_RemainsBottomAttachedWhenBottomExceedsWorkArea()
    {
        CodexWindowState state = State(new PixelRect(50, 100, 1450, 1050));
        CompanionLayoutDecision result = CompanionLayoutPlanner.ForNormalWindow(state);

        Assert.Equal(CompanionLayoutMode.BottomAttached, result.Companion.Mode);
        Assert.Equal(50, result.Companion.Left);
        Assert.Equal(1400, result.Companion.Width);
        Assert.Equal(1048, result.Companion.Top);
        Assert.Equal(50, result.Companion.Height);
    }

    [Fact]
    public void ForNormalWindow_RemainsBottomAttachedBeyondScreenEdge()
    {
        CodexWindowState state = State(new PixelRect(50, 100, 850, 1050));

        CompanionLayoutDecision result = CompanionLayoutPlanner.ForNormalWindow(state);

        Assert.Equal(CompanionLayoutMode.BottomAttached, result.Companion.Mode);
        Assert.Equal(1048, result.Companion.Top);
    }

    [Theory]
    [InlineData(true, true, CompanionLayoutMode.NativeIntegratedFullscreen)]
    [InlineData(false, true, CompanionLayoutMode.ManagedIntegratedFullscreen)]
    [InlineData(false, false, CompanionLayoutMode.FullscreenOverlay)]
    public void SelectFullscreenMode_PrefersNativeThenManagedThenOverlay(
        bool nativeAccepted,
        bool managedAccepted,
        CompanionLayoutMode expected)
    {
        Assert.Equal(
            expected,
            CompanionLayoutPlanner.SelectFullscreenMode(nativeAccepted, managedAccepted));
    }

    [Theory]
    [InlineData(96, 50)]
    [InlineData(120, 63)]
    [InlineData(144, 75)]
    [InlineData(192, 100)]
    public void CreateFullscreenOverlay_KeepsCompleteBarInsideCodexBottom(
        uint dpi,
        int expectedHeight)
    {
        PixelRect bounds = new(0, 0, 1920, 1080);

        CompanionLayoutDecision result = CompanionLayoutPlanner.CreateFullscreenOverlay(
            bounds,
            dpi);

        Assert.Equal(CompanionLayoutMode.FullscreenOverlay, result.Companion.Mode);
        Assert.Equal(1080 - expectedHeight, result.Companion.Top);
        Assert.Equal(1080, result.Companion.Top + result.Companion.Height);
        Assert.Null(result.CodexTarget);
    }

    [Fact]
    public void LayoutModes_ContainNoTitleBarOrHiddenFallback()
    {
        Assert.Equal(
            [
                CompanionLayoutMode.BottomAttached,
                CompanionLayoutMode.NativeIntegratedFullscreen,
                CompanionLayoutMode.ManagedIntegratedFullscreen,
                CompanionLayoutMode.FullscreenOverlay,
            ],
            Enum.GetValues<CompanionLayoutMode>());
    }

    private static CodexWindowState State(PixelRect bounds) => new(
        (nint)42,
        bounds,
        Dpi: 96,
        IsVisible: true,
        IsMinimized: false,
        @"C:\Program Files\WindowsApps\OpenAI.Codex_1\app\ChatGPT.exe");
}
