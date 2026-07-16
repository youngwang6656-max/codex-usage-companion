using CodexUsageCompanion.Protocol;
using System.Text.Json;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class CodexAppServerProtocolSessionTests
{
    [Fact]
    public async Task StartAsync_InitializesExperimentalApiThenSendsInitializedNotification()
    {
        var connection = new FakeConnection();
        await using var session = new CodexAppServerProtocolSession(connection);

        await session.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(connection.Started);
        Assert.Collection(
            connection.Calls,
            call =>
            {
                Assert.Equal("request:initialize", call.Method);
                Assert.Contains("codex-usage-companion", call.SerializedParameters, StringComparison.Ordinal);
                Assert.Contains("\"experimentalApi\":true", call.SerializedParameters, StringComparison.Ordinal);
            },
            call => Assert.Equal("notify:initialized", call.Method));
    }

    [Fact]
    public async Task ReadMethods_UseSchemaDefinedParameters()
    {
        var connection = new FakeConnection();
        await using var session = new CodexAppServerProtocolSession(connection);
        await session.StartAsync(TestContext.Current.CancellationToken);
        connection.Calls.Clear();

        _ = await session.ReadAccountAsync(TestContext.Current.CancellationToken);
        _ = await session.ReadRateLimitsAsync(TestContext.Current.CancellationToken);

        Assert.Collection(
            connection.Calls,
            call =>
            {
                Assert.Equal("request:account/read", call.Method);
                Assert.Equal("{\"refreshToken\":false}", call.SerializedParameters);
            },
            call =>
            {
                Assert.Equal("request:account/rateLimits/read", call.Method);
                Assert.Equal("null", call.SerializedParameters);
            });
    }

    [Fact]
    public async Task RateLimitNotification_ForwardsOnlyRelevantMethod()
    {
        var connection = new FakeConnection();
        await using var session = new CodexAppServerProtocolSession(connection);
        int notifications = 0;
        string? notificationJson = null;
        session.RateLimitsUpdated += (_, json) =>
        {
            notifications++;
            notificationJson = json;
        };
        await session.StartAsync(TestContext.Current.CancellationToken);

        connection.Notify("thread/started");
        connection.Notify(
            "account/rateLimits/updated",
            """{"rateLimits":{"planType":"plus"}}""");

        Assert.Equal(1, notifications);
        Assert.Equal("""{"rateLimits":{"planType":"plus"}}""", notificationJson);
    }

    [Fact]
    public async Task AccountUpdatedNotification_ForwardsNewPlanType()
    {
        var connection = new FakeConnection();
        await using var session = new CodexAppServerProtocolSession(connection);
        string? planType = null;
        session.AccountUpdated += (_, value) => planType = value;
        await session.StartAsync(TestContext.Current.CancellationToken);

        connection.Notify("account/updated", """{"authMode":"chatgpt","planType":"plus"}""");

        Assert.Equal("plus", planType);
    }

    [Fact]
    public async Task StartAsync_TimesOutWhenAppServerDoesNotRespond()
    {
        var connection = new FakeConnection { HangRequests = true };
        await using var session = new CodexAppServerProtocolSession(
            connection,
            requestTimeout: TimeSpan.FromMilliseconds(20));

        CodexAppServerException exception = await Assert.ThrowsAsync<CodexAppServerException>(
            async () => await session.StartAsync(TestContext.Current.CancellationToken));

        Assert.Equal("Codex app-server request timed out.", exception.Message);
    }

    private sealed class FakeConnection : IJsonRpcConnection
    {
        public event EventHandler<JsonRpcNotification>? NotificationReceived;

        public bool Started { get; private set; }

        public List<Call> Calls { get; } = [];

        public bool HangRequests { get; set; }

        public void Start() => Started = true;

        public async Task<string> RequestAsync(string method, object? parameters, CancellationToken cancellationToken)
        {
            Calls.Add(new Call($"request:{method}", JsonSerializer.Serialize(parameters)));
            if (HangRequests)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return "{}";
        }

        public Task NotifyAsync(string method, object? parameters, CancellationToken cancellationToken)
        {
            Calls.Add(new Call($"notify:{method}", JsonSerializer.Serialize(parameters)));
            return Task.CompletedTask;
        }

        public void Notify(string method, string? paramsJson = null)
        {
            JsonElement? parameters = paramsJson is null
                ? null
                : JsonDocument.Parse(paramsJson).RootElement.Clone();
            NotificationReceived?.Invoke(this, new JsonRpcNotification(method, parameters));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed record Call(string Method, string SerializedParameters);
}
