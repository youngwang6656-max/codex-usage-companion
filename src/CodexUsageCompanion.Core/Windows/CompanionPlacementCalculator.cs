namespace CodexUsageCompanion.Windows;

public static class CompanionPlacementCalculator
{
    private const double LogicalHeight = 50;
    private const double DefaultDpi = 96;

    public static CompanionPlacement Calculate(CodexWindowState window)
    {
        ArgumentNullException.ThrowIfNull(window);

        double scale = Math.Max(window.Dpi, 96) / DefaultDpi;
        int height = Scale(LogicalHeight, scale);
        return new CompanionPlacement(
            window.Bounds.Left,
            window.Bounds.Bottom - 2,
            window.Bounds.Width,
            height,
            CompanionLayoutMode.BottomAttached);
    }

    private static int Scale(double value, double scale) =>
        (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);
}
