namespace CodexUsageCompanion.Windows;

public static class CompanionLayoutPlanner
{
    private const double DefaultDpi = 96;
    private const double LogicalBarHeight = 50;
    private const int PhysicalOverlap = 2;

    public static CompanionLayoutDecision ForNormalWindow(CodexWindowState window)
    {
        ArgumentNullException.ThrowIfNull(window);

        int barHeight = Scale(LogicalBarHeight, window.Dpi);
        return new CompanionLayoutDecision(
            new CompanionPlacement(
                window.Bounds.Left,
                window.Bounds.Bottom - PhysicalOverlap,
                window.Bounds.Width,
                barHeight,
                CompanionLayoutMode.BottomAttached),
            CodexTarget: null);
    }

    public static CompanionLayoutDecision CreateIntegrated(
        PixelRect workArea,
        uint dpi,
        CompanionLayoutMode mode)
    {
        if (mode is not CompanionLayoutMode.NativeIntegratedFullscreen
            and not CompanionLayoutMode.ManagedIntegratedFullscreen)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        int barHeight = Scale(LogicalBarHeight, dpi);
        int companionTop = workArea.Bottom - barHeight;
        int codexBottom = companionTop + PhysicalOverlap;
        PixelRect codexTarget = new(
            workArea.Left,
            workArea.Top,
            workArea.Right,
            codexBottom);

        return new CompanionLayoutDecision(
            new CompanionPlacement(
                workArea.Left,
                companionTop,
                workArea.Width,
                barHeight,
                mode),
            codexTarget);
    }

    public static CompanionLayoutMode SelectFullscreenMode(
        bool nativeAccepted,
        bool managedAccepted) =>
        nativeAccepted
            ? CompanionLayoutMode.NativeIntegratedFullscreen
            : managedAccepted
                ? CompanionLayoutMode.ManagedIntegratedFullscreen
                : CompanionLayoutMode.FullscreenOverlay;

    public static CompanionLayoutDecision CreateFullscreenOverlay(
        PixelRect codexBounds,
        uint dpi)
    {
        int barHeight = Scale(LogicalBarHeight, dpi);
        return new CompanionLayoutDecision(
            new CompanionPlacement(
                codexBounds.Left,
                codexBounds.Bottom - barHeight,
                codexBounds.Width,
                barHeight,
                CompanionLayoutMode.FullscreenOverlay),
            CodexTarget: null);
    }

    private static int Scale(double value, uint dpi)
    {
        double scale = Math.Max(dpi, 96) / DefaultDpi;
        return (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);
    }
}
