namespace CodexUsageCompanion.Protocol;

public interface IJsonRpcConnection : IAsyncDisposable
{
    event EventHandler<JsonRpcNotification>? NotificationReceived;

    void Start();

    Task<string> RequestAsync(string method, object? parameters, CancellationToken cancellationToken);

    Task NotifyAsync(string method, object? parameters, CancellationToken cancellationToken);
}
