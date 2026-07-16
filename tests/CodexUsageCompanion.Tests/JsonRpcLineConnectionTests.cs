using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using CodexUsageCompanion.Protocol;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class JsonRpcLineConnectionTests
{
    [Fact]
    public async Task RequestAsync_PairsResponseById()
    {
        var reader = new ChannelTextReader();
        var writer = new CapturingTextWriter();
        await using var connection = new JsonRpcLineConnection(reader, writer);
        connection.Start();

        Task<string> responseTask = connection.RequestAsync(
            "account/read",
            new { refreshToken = false },
            TestContext.Current.CancellationToken);
        string request = await writer.WaitForLineAsync(TestContext.Current.CancellationToken);
        Assert.Equal("{\"id\":1,\"method\":\"account/read\",\"params\":{\"refreshToken\":false}}", request);

        await reader.AddAsync(
            "{\"id\":1,\"result\":{\"account\":{\"planType\":\"plus\"}}}",
            TestContext.Current.CancellationToken);

        string result = await responseTask;
        Assert.Equal("{\"account\":{\"planType\":\"plus\"}}", result);
    }

    [Fact]
    public async Task Notification_RaisesMethodAndClonedParameters()
    {
        var reader = new ChannelTextReader();
        var writer = new CapturingTextWriter();
        await using var connection = new JsonRpcLineConnection(reader, writer);
        var received = new TaskCompletionSource<JsonRpcNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.NotificationReceived += (_, notification) => received.TrySetResult(notification);
        connection.Start();

        await reader.AddAsync(
            "{\"method\":\"account/rateLimits/updated\",\"params\":{\"secret\":\"ignored\"}}",
            TestContext.Current.CancellationToken);

        JsonRpcNotification notification = await received.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        Assert.Equal("account/rateLimits/updated", notification.Method);
        Assert.Equal("ignored", notification.Params!.Value.GetProperty("secret").GetString());
    }

    [Fact]
    public async Task RequestAsync_ThrowsSanitizedServerError()
    {
        var reader = new ChannelTextReader();
        var writer = new CapturingTextWriter();
        await using var connection = new JsonRpcLineConnection(reader, writer);
        connection.Start();

        Task<string> responseTask = connection.RequestAsync(
            "account/rateLimits/read",
            null,
            TestContext.Current.CancellationToken);
        _ = await writer.WaitForLineAsync(TestContext.Current.CancellationToken);
        await reader.AddAsync(
            "{\"id\":1,\"error\":{\"code\":-32000,\"message\":\"unauthorized\"}}",
            TestContext.Current.CancellationToken);

        CodexAppServerException exception = await Assert.ThrowsAsync<CodexAppServerException>(
            async () => await responseTask);
        Assert.Equal("unauthorized", exception.Message);
    }

    private sealed class ChannelTextReader : TextReader
    {
        private readonly Channel<string?> _lines = Channel.CreateUnbounded<string?>();

        public ValueTask AddAsync(string line, CancellationToken cancellationToken) =>
            _lines.Writer.WriteAsync(line, cancellationToken);

        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) =>
            _lines.Reader.ReadAsync(cancellationToken);

        protected override void Dispose(bool disposing)
        {
            _lines.Writer.TryComplete();
            base.Dispose(disposing);
        }
    }

    private sealed class CapturingTextWriter : TextWriter
    {
        private readonly ConcurrentQueue<string> _lines = new();
        private readonly SemaphoreSlim _available = new(0);

        public override Encoding Encoding => Encoding.UTF8;

        public override Task WriteLineAsync(string? value)
        {
            _lines.Enqueue(value ?? string.Empty);
            _available.Release();
            return Task.CompletedTask;
        }

        public async Task<string> WaitForLineAsync(CancellationToken cancellationToken)
        {
            await _available.WaitAsync(cancellationToken);
            Assert.True(_lines.TryDequeue(out string? line));
            return line;
        }
    }
}
