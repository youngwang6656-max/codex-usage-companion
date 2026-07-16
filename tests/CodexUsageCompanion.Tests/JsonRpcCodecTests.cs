using CodexUsageCompanion.Protocol;
using System.Text.Json;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class JsonRpcCodecTests
{
    [Fact]
    public void CreateRequest_UsesCodexLineProtocolShape()
    {
        string json = JsonRpcCodec.CreateRequest(
            7,
            "account/read",
            new { refreshToken = false });

        Assert.Equal(
            "{\"id\":7,\"method\":\"account/read\",\"params\":{\"refreshToken\":false}}",
            json);
    }

    [Fact]
    public void CreateNotification_HasNoRequestId()
    {
        string json = JsonRpcCodec.CreateNotification("initialized", new { });

        Assert.Equal("{\"method\":\"initialized\",\"params\":{}}", json);
    }

    [Fact]
    public void Parse_ClonesResponseResultForUseAfterParsing()
    {
        JsonRpcMessage message = JsonRpcCodec.Parse("{\"id\":3,\"result\":{\"account\":null}}");

        Assert.Equal(3, message.Id);
        Assert.Equal(JsonRpcMessageKind.Response, message.Kind);
        Assert.Equal(JsonValueKind.Null, message.Result!.Value.GetProperty("account").ValueKind);
    }

    [Fact]
    public void Parse_ReadsRateLimitNotification()
    {
        JsonRpcMessage message = JsonRpcCodec.Parse(
            "{\"method\":\"account/rateLimits/updated\",\"params\":{\"rateLimits\":{}}}");

        Assert.Equal(JsonRpcMessageKind.Notification, message.Kind);
        Assert.Equal("account/rateLimits/updated", message.Method);
        Assert.Equal(
            JsonValueKind.Object,
            message.Params!.Value.GetProperty("rateLimits").ValueKind);
    }

    [Fact]
    public void Parse_ReadsServerErrorWithoutLeakingPayloadIntoException()
    {
        JsonRpcMessage message = JsonRpcCodec.Parse(
            "{\"id\":4,\"error\":{\"code\":-32000,\"message\":\"unauthorized\"}}");

        Assert.Equal(JsonRpcMessageKind.Error, message.Kind);
        Assert.Equal(4, message.Id);
        Assert.Equal("unauthorized", message.ErrorMessage);
    }
}
