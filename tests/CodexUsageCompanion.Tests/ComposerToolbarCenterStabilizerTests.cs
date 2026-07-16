using CodexUsageCompanion.Windows;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class ComposerToolbarCenterStabilizerTests
{
    private static readonly PixelRect Window = new(100, 200, 1300, 1100);

    [Fact]
    public void FirstSampleBecomesStableCenterImmediately()
    {
        var stabilizer = new ComposerToolbarCenterStabilizer();

        Assert.Equal(1000, stabilizer.Observe(Window, 96, 1000));
        Assert.True(stabilizer.TryResolve(Window, 96, out int centerY));
        Assert.Equal(1000, centerY);
    }

    [Fact]
    public void AlternatingHighAndLowSamplesNeverReplaceStableCenter()
    {
        var stabilizer = new ComposerToolbarCenterStabilizer();
        _ = stabilizer.Observe(Window, 96, 1000);

        Assert.Equal(1000, stabilizer.Observe(Window, 96, 970));
        Assert.Equal(1000, stabilizer.Observe(Window, 96, 1030));
        Assert.Equal(1000, stabilizer.Observe(Window, 96, 970));
        Assert.Equal(1000, stabilizer.Observe(Window, 96, 1030));
    }

    [Fact]
    public void LaterConsistentSamplesCannotRelatchTheSameLayout()
    {
        var stabilizer = new ComposerToolbarCenterStabilizer();
        _ = stabilizer.Observe(Window, 96, 1000);

        Assert.Equal(1000, stabilizer.Observe(Window, 96, 1020));
        Assert.Equal(1000, stabilizer.Observe(Window, 96, 1021));
        Assert.Equal(1000, stabilizer.Observe(Window, 96, 1019));
    }

    [Fact]
    public void SmallDpiScaledJitterKeepsOriginalCenter()
    {
        var stabilizer = new ComposerToolbarCenterStabilizer();
        PixelRect window = new(200, 400, 2600, 2200);
        _ = stabilizer.Observe(window, 192, 2000);

        Assert.Equal(2000, stabilizer.Observe(window, 192, 2004));
        Assert.Equal(2000, stabilizer.Observe(window, 192, 1996));
    }

    [Fact]
    public void StableCenterTranslatesWhenWindowMoves()
    {
        var stabilizer = new ComposerToolbarCenterStabilizer();
        _ = stabilizer.Observe(Window, 96, 1000);

        PixelRect moved = new(300, 400, 1500, 1300);
        Assert.True(stabilizer.TryResolve(moved, 96, out int centerY));
        Assert.Equal(1200, centerY);
    }

    [Fact]
    public void ResizeKeepsTheFrozenBottomRelativeCenter()
    {
        var stabilizer = new ComposerToolbarCenterStabilizer();
        _ = stabilizer.Observe(Window, 96, 1000);
        PixelRect resized = new(100, 200, 1400, 1200);

        Assert.Equal(1100, stabilizer.Observe(resized, 96, 1015));
    }
}
