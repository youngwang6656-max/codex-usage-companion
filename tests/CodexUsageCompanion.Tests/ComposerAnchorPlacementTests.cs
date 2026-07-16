using CodexUsageCompanion.Windows;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class ComposerAnchorPlacementTests
{
    [Theory]
    [InlineData(96u, 0, 0, 1200, 900, 889, 848, 917, 876)]
    [InlineData(192u, 0, 0, 2400, 1800, 1778, 1696, 1834, 1752)]
    public void FixedFallbackUsesDpiScaledRightBottomModelRowPosition(
        uint dpi,
        int left,
        int top,
        int right,
        int bottom,
        int expectedLeft,
        int expectedTop,
        int expectedRight,
        int expectedBottom)
    {
        ComposerAnchor anchor = ComposerAnchorPlacement.CreateFallback(
            new PixelRect(left, top, right, bottom),
            dpi);

        Assert.Equal(
            new PixelRect(expectedLeft, expectedTop, expectedRight, expectedBottom),
            anchor.ButtonBounds);
        Assert.Equal(ComposerAnchorSource.FixedFallback, anchor.Source);
    }

    [Fact]
    public void FixedFallbackUsesFullscreenComposerRowCenter()
    {
        ComposerAnchor anchor = ComposerAnchorPlacement.CreateFallback(
            new PixelRect(-13, -13, 2893, 1837),
            192);

        Assert.Equal(
            new PixelRect(2018, 1720, 2074, 1776),
            anchor.ButtonBounds);
    }

    [Fact]
    public void FixedFallbackClampsRightInsetForNarrowComposer()
    {
        ComposerAnchor anchor = ComposerAnchorPlacement.CreateFallback(
            new PixelRect(0, 0, 900, 700),
            96);

        Assert.Equal(
            new PixelRect(652, 648, 680, 676),
            anchor.ButtonBounds);
    }

    [Theory]
    [InlineData(96u, 500, 800, 650, 832, 462, 802, 490, 830)]
    [InlineData(120u, 625, 1000, 812, 1040, 577, 1003, 612, 1038)]
    [InlineData(144u, 750, 1200, 975, 1248, 693, 1203, 735, 1245)]
    [InlineData(192u, 1000, 1600, 1300, 1664, 924, 1604, 980, 1660)]
    public void ModelSelectorAnchorUsesTenDipGapAndVerticalCenter(
        uint dpi,
        int selectorLeft,
        int selectorTop,
        int selectorRight,
        int selectorBottom,
        int expectedLeft,
        int expectedTop,
        int expectedRight,
        int expectedBottom)
    {
        ComposerAnchor anchor = ComposerAnchorPlacement.CreateForModelSelector(
            new PixelRect(selectorLeft, selectorTop, selectorRight, selectorBottom),
            new PixelRect(0, 0, 2400, 1800),
            dpi,
            confidence: 0.95,
            rowCenterY: selectorTop + ((selectorBottom - selectorTop) / 2));

        Assert.Equal(
            new PixelRect(expectedLeft, expectedTop, expectedRight, expectedBottom),
            anchor.ButtonBounds);
        Assert.Equal(ComposerAnchorSource.Automation, anchor.Source);
    }

    [Fact]
    public void ModelSelectorAnchorUsesToolbarCenterInsteadOfAutomationFrameCenter()
    {
        ComposerAnchor anchor = ComposerAnchorPlacement.CreateForModelSelector(
            new PixelRect(400, 80, 800, 134),
            new PixelRect(0, 0, 1200, 900),
            192,
            confidence: 0.95,
            rowCenterY: 94);

        Assert.Equal(new PixelRect(324, 66, 380, 122), anchor.ButtonBounds);
    }

    [Fact]
    public void ModelSelectorAnchorFallsBackToAutomationFrameCenterWithoutToolbarCenter()
    {
        ComposerAnchor anchor = ComposerAnchorPlacement.CreateForModelSelector(
            new PixelRect(400, 80, 800, 134),
            new PixelRect(0, 0, 1200, 900),
            192,
            confidence: 0.95);

        Assert.Equal(new PixelRect(324, 79, 380, 135), anchor.ButtonBounds);
    }

    [Theory]
    [InlineData(96u, 1090, 844, 758, 830, 786, 858)]
    [InlineData(192u, 2180, 1688, 1516, 1660, 1572, 1716)]
    public void ComposerFallbackPlacesButtonLeftOfEstimatedModelSelector(
        uint dpi,
        int sendCenterX,
        int sendCenterY,
        int expectedLeft,
        int expectedTop,
        int expectedRight,
        int expectedBottom)
    {
        var landmarks = new ComposerLandmarks(
            sendCenterX,
            sendCenterY,
            ComposerAnchorPlacement.Scale(24, dpi),
            sendCenterX + ComposerAnchorPlacement.Scale(92, dpi),
            0.9);

        ComposerAnchor anchor = ComposerAnchorPlacement.CreateFromComposerLandmarks(
            new PixelRect(0, 0, 2400, 1800),
            landmarks,
            dpi);

        Assert.Equal(
            new PixelRect(expectedLeft, expectedTop, expectedRight, expectedBottom),
            anchor.ButtonBounds);
        Assert.Equal(ComposerAnchorSource.ComposerFallback, anchor.Source);
    }

    [Fact]
    public void CacheTranslatesAnchorWhenWindowOnlyMoves()
    {
        var cache = new ComposerAnchorCache();
        cache.Store(
            new PixelRect(100, 200, 1300, 1100),
            96,
            new ComposerAnchor(
                new PixelRect(1212, 1002, 1240, 1030),
                0.9,
                ComposerAnchorSource.Automation));

        bool found = cache.TryResolve(
            new PixelRect(300, 400, 1500, 1300),
            96,
            out ComposerAnchor translated);

        Assert.True(found);
        Assert.Equal(new PixelRect(1412, 1202, 1440, 1230), translated.ButtonBounds);
        Assert.Equal(ComposerAnchorSource.CachedAutomation, translated.Source);
    }

    [Fact]
    public void CachePreservesComposerFallbackSourceWhileWindowMoves()
    {
        var cache = new ComposerAnchorCache();
        cache.Store(
            new PixelRect(100, 200, 1300, 1100),
            96,
            new ComposerAnchor(
                new PixelRect(450, 1002, 478, 1030),
                0.7,
                ComposerAnchorSource.ComposerFallback));

        Assert.True(cache.TryResolve(
            new PixelRect(300, 400, 1500, 1300),
            96,
            out ComposerAnchor translated));
        Assert.Equal(ComposerAnchorSource.ComposerFallback, translated.Source);
    }

    [Fact]
    public void CacheTracksRightAndBottomEdgesAcrossResize()
    {
        var cache = new ComposerAnchorCache();
        cache.Store(
            new PixelRect(100, 200, 1300, 1100),
            96,
            new ComposerAnchor(
                new PixelRect(1212, 1002, 1240, 1030),
                0.9,
                ComposerAnchorSource.Automation));

        Assert.True(cache.TryResolve(
            new PixelRect(100, 200, 1500, 1200),
            96,
            out ComposerAnchor resized));
        Assert.Equal(new PixelRect(1412, 1102, 1440, 1130), resized.ButtonBounds);
    }

    [Fact]
    public void CacheRejectsDpiChanges()
    {
        var cache = new ComposerAnchorCache();
        cache.Store(
            new PixelRect(100, 200, 1300, 1100),
            96,
            ComposerAnchorPlacement.CreateFallback(
                new PixelRect(100, 200, 1300, 1100),
                96));

        Assert.False(cache.TryResolve(
            new PixelRect(100, 200, 1300, 1100),
            120,
            out _));
    }

    [Fact]
    public void ToolbarCenterCacheTranslatesCenterWhenWindowMoves()
    {
        var cache = new ComposerToolbarCenterCache();
        cache.Store(
            new PixelRect(100, 200, 1300, 1100),
            96,
            centerY: 1000);

        Assert.True(cache.TryResolve(
            new PixelRect(300, 400, 1500, 1300),
            96,
            out int translatedCenterY));
        Assert.Equal(1200, translatedCenterY);
    }

    [Theory]
    [InlineData(96u, 100, 200, 1400, 1100)]
    [InlineData(120u, 100, 200, 1300, 1100)]
    public void ToolbarCenterCacheRejectsSizeOrDpiChanges(
        uint dpi,
        int left,
        int top,
        int right,
        int bottom)
    {
        var cache = new ComposerToolbarCenterCache();
        cache.Store(
            new PixelRect(100, 200, 1300, 1100),
            96,
            centerY: 1000);

        Assert.False(cache.TryResolve(
            new PixelRect(left, top, right, bottom),
            dpi,
            out _));
    }
}
