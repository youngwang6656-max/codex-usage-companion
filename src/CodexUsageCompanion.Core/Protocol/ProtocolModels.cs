using CodexUsageCompanion.Usage;

namespace CodexUsageCompanion.Protocol;

public sealed record AccountResult(string? PlanType, bool AuthenticationRequired);

public sealed record RateLimitsResult(
    IReadOnlyList<RateLimitWindow> Windows,
    int? AvailableResetCount,
    string? PlanType);

public sealed record JsonRpcNotification(string Method, System.Text.Json.JsonElement? Params);

public sealed class ProtocolException : Exception
{
    public ProtocolException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
