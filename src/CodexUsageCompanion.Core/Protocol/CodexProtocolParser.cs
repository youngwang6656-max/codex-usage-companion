using System.Text.Json;
using CodexUsageCompanion.Usage;

namespace CodexUsageCompanion.Protocol;

public static class CodexProtocolParser
{
    public static AccountResult ParseAccount(string json)
    {
        using JsonDocument document = Parse(json, "account/read");
        JsonElement root = document.RootElement;

        if (!root.TryGetProperty("account", out JsonElement account) || account.ValueKind == JsonValueKind.Null)
        {
            return new AccountResult(null, AuthenticationRequired: true);
        }

        string? planType = account.TryGetProperty("planType", out JsonElement plan)
            && plan.ValueKind == JsonValueKind.String
                ? plan.GetString()
                : null;

        return new AccountResult(planType, AuthenticationRequired: false);
    }

    public static RateLimitsResult ParseRateLimits(string json)
    {
        using JsonDocument document = Parse(json, "account/rateLimits/read");
        JsonElement root = document.RootElement;
        var windows = new HashSet<RateLimitWindow>();

        if (root.TryGetProperty("rateLimits", out JsonElement rateLimits))
        {
            CollectWindows(rateLimits, windows);
        }

        if (root.TryGetProperty("rateLimitsByLimitId", out JsonElement byLimitId))
        {
            CollectWindows(byLimitId, windows);
        }

        int? availableResetCount = null;
        if (root.TryGetProperty("rateLimitResetCredits", out JsonElement credits)
            && credits.ValueKind == JsonValueKind.Object
            && credits.TryGetProperty("availableCount", out JsonElement count)
            && count.TryGetInt32(out int parsedCount))
        {
            availableResetCount = parsedCount;
        }

        return new RateLimitsResult(
            windows.ToArray(),
            availableResetCount,
            FindPlanType(root));
    }

    public static string? ParseAccountUpdated(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using JsonDocument document = Parse(json, "account/updated");
        JsonElement root = document.RootElement;
        return root.TryGetProperty("planType", out JsonElement planType)
            && planType.ValueKind == JsonValueKind.String
                ? planType.GetString()
                : null;
    }

    private static JsonDocument Parse(string json, string method)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new ProtocolException($"Codex {method} returned malformed JSON.", exception);
        }
    }

    private static void CollectWindows(JsonElement element, ISet<RateLimitWindow> destination)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (element.TryGetProperty("usedPercent", out JsonElement usedPercent)
            && usedPercent.TryGetDouble(out double parsedUsedPercent))
        {
            long? resetsAt = element.TryGetProperty("resetsAt", out JsonElement reset)
                && reset.TryGetInt64(out long parsedReset)
                    ? parsedReset
                    : null;

            int? duration = element.TryGetProperty("windowDurationMins", out JsonElement durationElement)
                && durationElement.TryGetInt32(out int parsedDuration)
                    ? parsedDuration
                    : null;

            destination.Add(new RateLimitWindow(parsedUsedPercent, resetsAt, duration));
            return;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            CollectWindows(property.Value, destination);
        }
    }

    private static string? FindPlanType(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (element.TryGetProperty("planType", out JsonElement planType)
            && planType.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(planType.GetString()))
        {
            return planType.GetString();
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            string? nested = FindPlanType(property.Value);
            if (!string.IsNullOrWhiteSpace(nested))
            {
                return nested;
            }
        }

        return null;
    }
}
