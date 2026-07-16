using CodexUsageCompanion.Protocol;
using CodexUsageCompanion.Usage;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class CodexProtocolParserTests
{
    [Fact]
    public void ParseAccount_ReadsPlanTypeWithoutRetainingEmail()
    {
        const string json = """
            {"account":{"type":"chatgpt","email":"private@example.com","planType":"plus"}}
            """;

        AccountResult result = CodexProtocolParser.ParseAccount(json);

        Assert.Equal("plus", result.PlanType);
        Assert.DoesNotContain("private@example.com", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ParseAccount_RecognizesMissingAccountAsAuthenticationRequired()
    {
        AccountResult result = CodexProtocolParser.ParseAccount("{\"account\":null}");

        Assert.True(result.AuthenticationRequired);
        Assert.Null(result.PlanType);
    }

    [Fact]
    public void ParseRateLimits_ReadsPrimarySecondaryAndResetCredits()
    {
        const string json = """
            {
              "rateLimits": {
                "primary": {"usedPercent": 90, "resetsAt": 1784707200, "windowDurationMins": 300},
                "secondary": {"usedPercent": 54, "resetsAt": 1785312000, "windowDurationMins": 10080}
              },
              "rateLimitResetCredits": {"availableCount": 1}
            }
            """;

        RateLimitsResult result = CodexProtocolParser.ParseRateLimits(json);

        Assert.Equal(2, result.Windows.Count);
        Assert.Contains(result.Windows, window => window.WindowDurationMinutes == 300);
        Assert.Contains(result.Windows, window => window.WindowDurationMinutes == 10_080);
        Assert.Equal(1, result.AvailableResetCount);
        Assert.Null(result.PlanType);
    }

    [Fact]
    public void ParseRateLimits_IncludesNamedLimitWindowsAndDeduplicatesIdenticalWindows()
    {
        const string json = """
            {
              "rateLimits": {
                "primary": null,
                "secondary": {"usedPercent": 54, "resetsAt": 1785312000, "windowDurationMins": 10080}
              },
              "rateLimitsByLimitId": {
                "weekly": {"primary": {"usedPercent": 54, "resetsAt": 1785312000, "windowDurationMins": 10080}}
              }
            }
            """;

        RateLimitsResult result = CodexProtocolParser.ParseRateLimits(json);

        Assert.Single(result.Windows);
        Assert.Null(result.AvailableResetCount);
    }

    [Fact]
    public void ParseRateLimits_RejectsMalformedJson()
    {
        Assert.Throws<ProtocolException>(() => CodexProtocolParser.ParseRateLimits("not-json"));
    }

    [Fact]
    public void ParseRateLimits_ReadsPlanTypeFromRateLimitSnapshot()
    {
        const string json = """
            {
              "rateLimits": {
                "planType": "plus",
                "secondary": {"usedPercent": 54, "resetsAt": 1785312000, "windowDurationMins": 10080}
              }
            }
            """;

        RateLimitsResult result = CodexProtocolParser.ParseRateLimits(json);

        Assert.Equal("plus", result.PlanType);
    }

    [Fact]
    public void ParseAccountUpdated_ReadsNullablePlanType()
    {
        Assert.Equal(
            "pro",
            CodexProtocolParser.ParseAccountUpdated("""{"authMode":"chatgpt","planType":"pro"}"""));
        Assert.Null(CodexProtocolParser.ParseAccountUpdated("""{"authMode":null,"planType":null}"""));
    }
}
