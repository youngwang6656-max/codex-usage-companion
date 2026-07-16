namespace CodexUsageCompanion.Protocol;

public interface ICodexAppServerSession : IAsyncDisposable
{
    event EventHandler? RateLimitsUpdated;

    event EventHandler<string?>? AccountUpdated;

    Task StartAsync(CancellationToken cancellationToken);

    Task<string> ReadAccountAsync(CancellationToken cancellationToken);

    Task<string> ReadRateLimitsAsync(CancellationToken cancellationToken);
}
