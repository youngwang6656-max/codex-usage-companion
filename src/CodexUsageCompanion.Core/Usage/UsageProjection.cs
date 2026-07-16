namespace CodexUsageCompanion.Usage;

public static class UsageProjection
{
    private const int MinimumWeeklyWindowMinutes = 6 * 24 * 60;

    private static readonly IReadOnlyDictionary<string, string> PlanLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["free"] = "Free",
            ["go"] = "Go",
            ["plus"] = "Plus",
            ["pro"] = "Pro",
            ["pro_lite"] = "Pro Lite",
            ["team"] = "Team",
            ["self_serve_business"] = "Business",
            ["business"] = "Business",
            ["enterprise"] = "Enterprise",
            ["enterprise_cbp_usage_based"] = "Enterprise",
            ["enterprise_usage_based"] = "Enterprise",
            ["edu"] = "Edu",
        };

    public static UsageSnapshot Create(
        string? planType,
        IEnumerable<RateLimitWindow> windows,
        int? availableResetCount,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(windows);

        RateLimitWindow? weeklyWindow = windows
            .Where(window => window.WindowDurationMinutes >= MinimumWeeklyWindowMinutes)
            .OrderByDescending(window => window.WindowDurationMinutes)
            .FirstOrDefault();

        int? remainingPercent = weeklyWindow is null
            ? null
            : (int)Math.Round(
                Math.Clamp(100 - weeklyWindow.UsedPercent, 0, 100),
                MidpointRounding.AwayFromZero);

        DateTimeOffset? resetsAt = weeklyWindow?.ResetsAtUnixSeconds is long unixSeconds
            ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
            : null;

        string? planLabel = GetPlanLabel(planType);

        return new UsageSnapshot(
            planLabel,
            remainingPercent,
            resetsAt,
            availableResetCount,
            SyncState.Synced,
            now);
    }

    public static string? GetPlanLabel(string? planType) =>
        planType is not null && PlanLabels.TryGetValue(planType, out string? label)
            ? label
            : null;
}
