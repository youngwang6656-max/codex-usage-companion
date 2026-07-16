namespace CodexUsageCompanion.Windows;

public enum AnchorCalibrationAction
{
    None,
    Defer,
    Animate,
}

public readonly record struct AnchorCalibrationPlan(
    AnchorCalibrationAction Action,
    TimeSpan Duration);

public static class AnchorCalibrationPlanner
{
    private static readonly TimeSpan CalibrationDuration =
        TimeSpan.FromMilliseconds(100);

    public static AnchorCalibrationPlan Create(
        PixelRect current,
        PixelRect target,
        bool isMoving)
    {
        int deviation = Math.Max(
            Math.Max(
                Math.Abs(current.Left - target.Left),
                Math.Abs(current.Top - target.Top)),
            Math.Max(
                Math.Abs(current.Right - target.Right),
                Math.Abs(current.Bottom - target.Bottom)));
        if (deviation <= 2)
        {
            return new AnchorCalibrationPlan(
                AnchorCalibrationAction.None,
                TimeSpan.Zero);
        }

        return isMoving
            ? new AnchorCalibrationPlan(
                AnchorCalibrationAction.Defer,
                TimeSpan.Zero)
            : new AnchorCalibrationPlan(
                AnchorCalibrationAction.Animate,
                CalibrationDuration);
    }
}
