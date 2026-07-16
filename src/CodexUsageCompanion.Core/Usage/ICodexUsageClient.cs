namespace CodexUsageCompanion.Usage;

public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}

public interface ICodexUsageClient : IAsyncDisposable
{
    event EventHandler<UsageSnapshot>? SnapshotChanged;

    UsageSnapshot CurrentSnapshot { get; }

    Task StartAsync(CancellationToken cancellationToken);

    Task RefreshAsync(CancellationToken cancellationToken);
}
