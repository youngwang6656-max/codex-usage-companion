namespace CodexUsageCompanion.Windows;

public readonly record struct RoundRegionGeometry(
    int Right,
    int Bottom,
    int EllipseWidth,
    int EllipseHeight);

public static class WindowRegionGeometry
{
    private const double LogicalHeight = 50;
    private const double LogicalEllipseDiameter = 26;

    public static RoundRegionGeometry FromPlacement(CompanionPlacement placement)
    {
        int ellipseDiameter = (int)Math.Round(
            LogicalEllipseDiameter * placement.Height / LogicalHeight,
            MidpointRounding.AwayFromZero);
        return new RoundRegionGeometry(
            placement.Width + 1,
            placement.Height + 1,
            ellipseDiameter,
            ellipseDiameter);
    }
}
