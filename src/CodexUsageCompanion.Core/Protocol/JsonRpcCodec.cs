using System.Text.Json;

namespace CodexUsageCompanion.Protocol;

public enum JsonRpcMessageKind
{
    Response,
    Notification,
    Error,
}

public sealed record JsonRpcMessage(
    JsonRpcMessageKind Kind,
    int? Id,
    string? Method,
    JsonElement? Result,
    JsonElement? Params,
    string? ErrorMessage);

public static class JsonRpcCodec
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string CreateRequest(int id, string method, object? parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        return JsonSerializer.Serialize(new { id, method, @params = parameters }, SerializerOptions);
    }

    public static string CreateNotification(string method, object? parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        return JsonSerializer.Serialize(new { method, @params = parameters }, SerializerOptions);
    }

    public static JsonRpcMessage Parse(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            int? id = root.TryGetProperty("id", out JsonElement idElement)
                && idElement.TryGetInt32(out int parsedId)
                    ? parsedId
                    : null;

            if (root.TryGetProperty("error", out JsonElement error))
            {
                string? message = error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out JsonElement messageElement)
                        ? messageElement.GetString()
                        : "Codex app-server request failed.";

                return new JsonRpcMessage(JsonRpcMessageKind.Error, id, null, null, null, message);
            }

            if (id is not null && root.TryGetProperty("result", out JsonElement result))
            {
                return new JsonRpcMessage(JsonRpcMessageKind.Response, id, null, result.Clone(), null, null);
            }

            if (root.TryGetProperty("method", out JsonElement methodElement))
            {
                return new JsonRpcMessage(
                    JsonRpcMessageKind.Notification,
                    null,
                    methodElement.GetString(),
                    null,
                    root.TryGetProperty("params", out JsonElement parameters)
                        ? parameters.Clone()
                        : null,
                    null);
            }

            throw new ProtocolException("Codex app-server returned an unsupported message.");
        }
        catch (JsonException exception)
        {
            throw new ProtocolException("Codex app-server returned malformed JSON.", exception);
        }
    }
}
