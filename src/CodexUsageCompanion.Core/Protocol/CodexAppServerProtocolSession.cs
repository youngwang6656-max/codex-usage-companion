namespace CodexUsageCompanion.Protocol;

public sealed class CodexAppServerProtocolSession : ICodexAppServerSession
{
    private readonly IJsonRpcConnection _connection;
    private readonly TimeSpan _requestTimeout;
    private bool _started;
    private bool _disposed;

    public CodexAppServerProtocolSession(
        IJsonRpcConnection connection,
        TimeSpan? requestTimeout = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(10);
        if (_requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }
    }

    public event EventHandler? RateLimitsUpdated;

    public event EventHandler<string?>? AccountUpdated;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        _connection.NotificationReceived += OnNotificationReceived;
        _connection.Start();

        var initializeParams = new
        {
            clientInfo = new
            {
                name = "codex-usage-companion",
                title = "Codex Usage Companion",
                version = "1.0.0",
            },
            capabilities = new
            {
                experimentalApi = true,
            },
        };

        _ = await RequestWithTimeoutAsync("initialize", initializeParams, cancellationToken)
            .ConfigureAwait(false);
        await _connection.NotifyAsync("initialized", new { }, cancellationToken).ConfigureAwait(false);
        _started = true;
    }

    public Task<string> ReadAccountAsync(CancellationToken cancellationToken)
    {
        EnsureStarted();
        return RequestWithTimeoutAsync(
            "account/read",
            new { refreshToken = false },
            cancellationToken);
    }

    public Task<string> ReadRateLimitsAsync(CancellationToken cancellationToken)
    {
        EnsureStarted();
        return RequestWithTimeoutAsync("account/rateLimits/read", null, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection.NotificationReceived -= OnNotificationReceived;
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    private void EnsureStarted()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_started)
        {
            throw new InvalidOperationException("Codex app-server session has not been initialized.");
        }
    }

    private void OnNotificationReceived(object? sender, JsonRpcNotification notification)
    {
        if (string.Equals(
                notification.Method,
                "account/rateLimits/updated",
                StringComparison.Ordinal))
        {
            RateLimitsUpdated?.Invoke(this, EventArgs.Empty);
        }
        else if (string.Equals(notification.Method, "account/updated", StringComparison.Ordinal))
        {
            string? planType = CodexProtocolParser.ParseAccountUpdated(
                notification.Params?.GetRawText());
            AccountUpdated?.Invoke(this, planType);
        }
    }

    private async Task<string> RequestWithTimeoutAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_requestTimeout);
        try
        {
            return await _connection.RequestAsync(method, parameters, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CodexAppServerException("Codex app-server request timed out.");
        }
    }
}
