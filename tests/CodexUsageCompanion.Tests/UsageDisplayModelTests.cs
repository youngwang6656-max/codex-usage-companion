using CodexUsageCompanion.Usage;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class UsageDisplayModelTests
{
    private static readonly TimeZoneInfo China = TimeZoneInfo.CreateCustomTimeZone(
        "China",
        TimeSpan.FromHours(8),
        "China",
        "China");

    [Fact]
    public void FromSnapshot_FormatsApprovedSingleLineCopy()
    {
        var snapshot = new UsageSnapshot(
            "Plus",
            46,
            new DateTimeOffset(2026, 7, 21, 16, 0, 0, TimeSpan.Zero),
            1,
            SyncState.Synced,
            DateTimeOffset.UtcNow);

        UsageDisplayModel model = UsageDisplayModel.FromSnapshot(snapshot, China);

        Assert.Equal("Codex · Plus", model.AccountText);
        Assert.Equal("已同步", model.StatusText);
        Assert.True(model.IsSynced);
        Assert.Equal("每周使用限制", model.LimitLabel);
        Assert.Equal("剩余 46%", model.RemainingText);
        Assert.Equal(46, model.ProgressValue);
        Assert.True(model.IsProgressKnown);
        Assert.Equal("重置时间 7月22日", model.ResetTimeText);
        Assert.Equal("可用重置 1次", model.ResetCreditsText);
    }

    [Fact]
    public void FromSnapshot_DistinguishesZeroCreditsFromUnknownCredits()
    {
        UsageSnapshot basis = new("Plus", 46, null, 0, SyncState.Synced, DateTimeOffset.UtcNow);

        UsageDisplayModel zero = UsageDisplayModel.FromSnapshot(basis, China);
        UsageDisplayModel unknown = UsageDisplayModel.FromSnapshot(
            basis with { AvailableResetCount = null },
            China);

        Assert.Equal("可用重置 0次", zero.ResetCreditsText);
        Assert.Equal("可用重置 —", unknown.ResetCreditsText);
    }

    [Theory]
    [InlineData(SyncState.Syncing, "同步中")]
    [InlineData(SyncState.Offline, "未同步")]
    [InlineData(SyncState.AuthenticationRequired, "请先登录 Codex")]
    [InlineData(SyncState.Disabled, "已关闭")]
    public void FromSnapshot_UsesGrayStatusForAnythingOtherThanSynced(
        SyncState state,
        string expectedStatus)
    {
        var snapshot = new UsageSnapshot(null, null, null, null, state, DateTimeOffset.UtcNow);

        UsageDisplayModel model = UsageDisplayModel.FromSnapshot(snapshot, China);

        Assert.Equal("Codex", model.AccountText);
        Assert.Equal(expectedStatus, model.StatusText);
        Assert.False(model.IsSynced);
        Assert.Equal("剩余 —", model.RemainingText);
        Assert.Equal("重置时间 —", model.ResetTimeText);
        Assert.Equal("可用重置 —", model.ResetCreditsText);
        Assert.False(model.IsProgressKnown);
    }
}
