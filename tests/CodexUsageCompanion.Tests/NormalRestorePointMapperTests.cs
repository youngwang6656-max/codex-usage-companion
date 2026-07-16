using CodexUsageCompanion.Windows;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class NormalRestorePointMapperTests
{
    [Fact]
    public void MapToAvailableMonitor_PreservesLogicalSizeAcrossDpiAndClampsToWorkArea()
    {
        var point = new NormalWindowRestorePoint(
            (nint)7,
            new PixelRect(3_000, 200, 4_200, 950),
            144,
            NormalRestorePointSource.ObservedNormal);
        var workArea = new PixelRect(0, 0, 1920, 1040);

        NormalWindowRestorePoint mapped = NormalRestorePointMapper.MapToWorkArea(
            point,
            workArea,
            targetDpi: 96);

        Assert.Equal(new PixelRect(1120, 200, 1920, 700), mapped.Bounds);
        Assert.Equal(96u, mapped.Dpi);
    }

    [Fact]
    public void MapToAvailableMonitor_NeverCreatesBoundsLargerThanWorkArea()
    {
        var point = new NormalWindowRestorePoint(
            (nint)7,
            new PixelRect(-3_000, -2_000, 1_000, 2_000),
            96,
            NormalRestorePointSource.WindowPlacement);
        var workArea = new PixelRect(100, 50, 1700, 950);

        NormalWindowRestorePoint mapped = NormalRestorePointMapper.MapToWorkArea(
            point,
            workArea,
            targetDpi: 192);

        Assert.Equal(workArea, mapped.Bounds);
    }
}
