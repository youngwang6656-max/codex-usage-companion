using CodexUsageCompanion.Usage;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class UsageProjectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_UsesWeeklyWindowAndIgnoresLegacyFiveHourWindow()
    {
        RateLimitWindow[] windows =
        [
            new(UsedPercent: 90, ResetsAtUnixSeconds: Now.AddHours(3).ToUnixTimeSeconds(), WindowDurationMinutes: 300),
            new(UsedPercent: 54, ResetsAtUnixSeconds: Now.AddDays(7).ToUnixTimeSeconds(), WindowDurationMinutes: 10_080),
        ];

        UsageSnapshot snapshot = UsageProjection.Create("plus", windows, availableResetCount: 1, Now);

        Assert.Equal(46, snapshot.RemainingPercent);
        Assert.Equal(Now.AddDays(7), snapshot.ResetsAt);
        Assert.Equal("Plus", snapshot.PlanLabel);
    }

    [Theory]
    [InlineData("plus", "Plus")]
    [InlineData("pro", "Pro")]
    [InlineData("team", "Team")]
    [InlineData("self_serve_business", "Business")]
    [InlineData("unknown", null)]
    [InlineData(null, null)]
    public void Create_MapsPlanWithoutHardCodingMembership(string? planType, string? expected)
    {
        UsageSnapshot snapshot = UsageProjection.Create(planType, [], availableResetCount: null, Now);

        Assert.Equal(expected, snapshot.PlanLabel);
    }

    [Fact]
    public void Create_PreservesZeroResetCredits()
    {
        UsageSnapshot snapshot = UsageProjection.Create("plus", [], availableResetCount: 0, Now);

        Assert.Equal(0, snapshot.AvailableResetCount);
    }

    [Fact]
    public void Create_LeavesMissingResetCreditsUnknown()
    {
        UsageSnapshot snapshot = UsageProjection.Create("plus", [], availableResetCount: null, Now);

        Assert.Null(snapshot.AvailableResetCount);
    }

    [Theory]
    [InlineData(-10, 100)]
    [InlineData(54.4, 46)]
    [InlineData(140, 0)]
    public void Create_ClampsAndRoundsRemainingPercentage(double usedPercent, int expected)
    {
        RateLimitWindow[] windows =
        [
            new(usedPercent, Now.AddDays(7).ToUnixTimeSeconds(), 10_080),
        ];

        UsageSnapshot snapshot = UsageProjection.Create("plus", windows, availableResetCount: null, Now);

        Assert.Equal(expected, snapshot.RemainingPercent);
    }

    [Fact]
    public void Create_DoesNotExposeShortWindowWhenWeeklyWindowIsMissing()
    {
        RateLimitWindow[] windows =
        [
            new(UsedPercent: 25, ResetsAtUnixSeconds: Now.AddHours(5).ToUnixTimeSeconds(), WindowDurationMinutes: 300),
        ];

        UsageSnapshot snapshot = UsageProjection.Create("plus", windows, availableResetCount: 1, Now);

        Assert.Null(snapshot.RemainingPercent);
        Assert.Null(snapshot.ResetsAt);
    }
}
