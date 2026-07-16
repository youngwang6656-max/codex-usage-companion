namespace CodexUsageCompanion.Windows;

public static class CompanionAnimationMath
{
    public static double CubicEaseInOut(double progress)
    {
        double value = Math.Clamp(progress, 0, 1);
        return value < 0.5
            ? 4 * value * value * value
            : 1 - Math.Pow(-2 * value + 2, 3) / 2;
    }

    public static TimeSpan ScaleDuration(
        TimeSpan fullDuration,
        double startProgress,
        double targetProgress)
    {
        double distance = Math.Abs(
            Math.Clamp(targetProgress, 0, 1)
            - Math.Clamp(startProgress, 0, 1));
        return TimeSpan.FromTicks((long)Math.Round(
            fullDuration.Ticks * distance,
            MidpointRounding.AwayFromZero));
    }

    public static int Interpolate(int start, int end, double progress) =>
        (int)Math.Round(
            start + ((end - start) * Math.Clamp(progress, 0, 1)),
            MidpointRounding.AwayFromZero);
}
