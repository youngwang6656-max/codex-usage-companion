namespace CodexUsageCompanion.Windows;

public static class NormalRestorePointMapper
{
    public static NormalWindowRestorePoint MapToWorkArea(
        NormalWindowRestorePoint restorePoint,
        PixelRect workArea,
        uint targetDpi)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            return restorePoint;
        }

        uint resolvedDpi = targetDpi == 0 ? 96u : targetDpi;
        double scale = resolvedDpi / (double)Math.Max(1u, restorePoint.Dpi);
        int width = Math.Clamp(
            (int)Math.Round(restorePoint.Bounds.Width * scale),
            1,
            workArea.Width);
        int height = Math.Clamp(
            (int)Math.Round(restorePoint.Bounds.Height * scale),
            1,
            workArea.Height);
        int left = Math.Clamp(
            restorePoint.Bounds.Left,
            workArea.Left,
            workArea.Right - width);
        int top = Math.Clamp(
            restorePoint.Bounds.Top,
            workArea.Top,
            workArea.Bottom - height);

        return restorePoint with
        {
            Bounds = new PixelRect(left, top, left + width, top + height),
            Dpi = resolvedDpi,
        };
    }
}
