namespace CodexUsageCompanion.Usage;

public sealed record UsageDisplayModel(
    string AccountText,
    string StatusText,
    bool IsSynced,
    string LimitLabel,
    string RemainingText,
    double ProgressValue,
    bool IsProgressKnown,
    string ResetTimeText,
    string ResetCreditsText)
{
    public static UsageDisplayModel FromSnapshot(UsageSnapshot snapshot, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(timeZone);

        string accountText = snapshot.PlanLabel is null
            ? "Codex"
            : $"Codex · {snapshot.PlanLabel}";
        string statusText = snapshot.State switch
        {
            SyncState.Synced => "已同步",
            SyncState.Syncing => "同步中",
            SyncState.AuthenticationRequired => "请先登录 Codex",
            SyncState.Disabled => "已关闭",
            _ => "未同步",
        };
        string remainingText = snapshot.RemainingPercent is int remaining
            ? $"剩余 {remaining}%"
            : "剩余 —";
        string resetTimeText = snapshot.ResetsAt is DateTimeOffset resetsAt
            ? FormatResetTime(resetsAt, timeZone)
            : "重置时间 —";
        string resetCreditsText = snapshot.AvailableResetCount is int credits
            ? $"可用重置 {credits}次"
            : "可用重置 —";

        return new UsageDisplayModel(
            accountText,
            statusText,
            snapshot.State == SyncState.Synced,
            "每周使用限制",
            remainingText,
            snapshot.RemainingPercent ?? 0,
            snapshot.RemainingPercent is not null,
            resetTimeText,
            resetCreditsText);
    }

    private static string FormatResetTime(DateTimeOffset resetsAt, TimeZoneInfo timeZone)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(resetsAt, timeZone);
        return $"重置时间 {local.Month}月{local.Day}日";
    }
}
