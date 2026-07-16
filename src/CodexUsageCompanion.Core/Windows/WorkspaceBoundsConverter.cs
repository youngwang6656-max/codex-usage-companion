namespace CodexUsageCompanion.Windows;

public static class WorkspaceBoundsConverter
{
    public static PixelRect ToScreen(
        PixelRect workspaceBounds,
        PixelRect monitorBounds,
        PixelRect workArea)
    {
        int offsetX = workArea.Left - monitorBounds.Left;
        int offsetY = workArea.Top - monitorBounds.Top;
        return new PixelRect(
            workspaceBounds.Left + offsetX,
            workspaceBounds.Top + offsetY,
            workspaceBounds.Right + offsetX,
            workspaceBounds.Bottom + offsetY);
    }
}
