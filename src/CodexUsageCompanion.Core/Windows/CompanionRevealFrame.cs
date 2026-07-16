namespace CodexUsageCompanion.Windows;

public readonly record struct CompanionRevealFrame(
    int VisibleTrayHeight,
    PixelRect ToggleBounds);

public static class CompanionButtonPlacement
{
    private const double HitTargetDip = 28;
    private const double RightInsetDip = 18;
    private const double ButtonGapDip = 4;

    public static PixelRect GetExpandedPowerButton(
        CompanionPlacement tray,
        uint dpi)
    {
        int size = ComposerAnchorPlacement.Scale(HitTargetDip, dpi);
        int right = tray.Left + tray.Width
            - ComposerAnchorPlacement.Scale(RightInsetDip, dpi);
        int top = tray.Top + ((tray.Height - size) / 2);
        return new PixelRect(right - size, top, right, top + size);
    }

    public static PixelRect GetExpandedRefreshButton(
        CompanionPlacement tray,
        uint dpi)
    {
        PixelRect power = GetExpandedPowerButton(tray, dpi);
        int size = ComposerAnchorPlacement.Scale(HitTargetDip, dpi);
        int right = power.Left - ComposerAnchorPlacement.Scale(ButtonGapDip, dpi);
        return new PixelRect(right - size, power.Top, right, power.Bottom);
    }
}

public static class CompanionRevealFrameCalculator
{
    public static CompanionRevealFrame Calculate(
        CompanionPlacement tray,
        ComposerAnchor collapsedAnchor,
        double visualProgress,
        uint dpi)
    {
        double progress = Math.Clamp(visualProgress, 0, 1);
        PixelRect expanded = CompanionButtonPlacement.GetExpandedPowerButton(tray, dpi);
        PixelRect collapsed = collapsedAnchor.ButtonBounds;
        return new CompanionRevealFrame(
            CompanionAnimationMath.Interpolate(0, tray.Height, progress),
            new PixelRect(
                CompanionAnimationMath.Interpolate(collapsed.Left, expanded.Left, progress),
                CompanionAnimationMath.Interpolate(collapsed.Top, expanded.Top, progress),
                CompanionAnimationMath.Interpolate(collapsed.Right, expanded.Right, progress),
                CompanionAnimationMath.Interpolate(collapsed.Bottom, expanded.Bottom, progress)));
    }
}
