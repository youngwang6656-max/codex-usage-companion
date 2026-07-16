using CodexUsageCompanion.Protocol;

namespace CodexUsageCompanion.Usage;

public sealed class CodexUsageClient : ICodexUsageClient
{
    private readonly ICodexAppServerSession _session;
    private readonly IClock _clock;
    private readonly TimeSpan _refreshInterval;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _pollingTask;
    private string? _notifiedPlanType;
    private bool _hasSuccessfulSnapshot;
    private bool _disposed;

    public CodexUsageClient(
        ICodexAppServerSession session,
        IClock clock,
        TimeSpan refreshInterval)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _refreshInterval = refreshInterval > TimeSpan.Zero
            ? refreshInterval
            : throw new ArgumentOutOfRangeException(nameof(refreshInterval));
        CurrentSnapshot = EmptySnapshot(SyncState.Syncing, _clock.Now);
    }

    public event EventHandler<UsageSnapshot>? SnapshotChanged;

    public UsageSnapshot CurrentSnapshot { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _session.RateLimitsUpdated += OnRateLimitsUpdated;
        _session.AccountUpdated += OnAccountUpdated;

        try
        {
            await _session.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            Publish(EmptySnapshot(SyncState.Offline, _clock.Now));
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        _pollingTask ??= PollAsync(_lifetime.Token);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AccountResult account = CodexProtocolParser.ParseAccount(
                await _session.ReadAccountAsync(cancellationToken).ConfigureAwait(false));

            if (account.AuthenticationRequired)
            {
                Publish(UsageFreshness.AuthenticationRequired(CurrentSnapshot, _clock.Now));
                return;
            }

            RateLimitsResult limits = CodexProtocolParser.ParseRateLimits(
                await _session.ReadRateLimitsAsync(cancellationToken).ConfigureAwait(false));

            UsageSnapshot snapshot = UsageProjection.Create(
                limits.PlanType ?? _notifiedPlanType ?? account.PlanType,
                limits.Windows,
                limits.AvailableResetCount,
                _clock.Now);

            _hasSuccessfulSnapshot = true;
            Publish(snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            UsageSnapshot failed = _hasSuccessfulSnapshot
                ? UsageFreshness.OnConnectionFailure(CurrentSnapshot, _clock.Now)
                : EmptySnapshot(SyncState.Offline, _clock.Now);
            Publish(failed);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.RateLimitsUpdated -= OnRateLimitsUpdated;
        _session.AccountUpdated -= OnAccountUpdated;
        _lifetime.Cancel();

        if (_pollingTask is not null)
        {
            try
            {
                await _pollingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        await _session.DisposeAsync().ConfigureAwait(false);
        _lifetime.Dispose();
        _refreshLock.Dispose();
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_refreshInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async void OnRateLimitsUpdated(object? sender, EventArgs eventArgs)
    {
        try
        {
            await RefreshAsync(_lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnAccountUpdated(object? sender, string? planType)
    {
        _notifiedPlanType = planType;
        if (string.IsNullOrWhiteSpace(planType))
        {
            Publish(UsageFreshness.AuthenticationRequired(CurrentSnapshot, _clock.Now));
            return;
        }

        UsageSnapshot updated = CurrentSnapshot with
        {
            PlanLabel = UsageProjection.GetPlanLabel(planType),
            UpdatedAt = _clock.Now,
        };
        Publish(updated);
    }

    private void Publish(UsageSnapshot snapshot)
    {
        CurrentSnapshot = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
    }

    private static UsageSnapshot EmptySnapshot(SyncState state, DateTimeOffset now) =>
        new(null, null, null, null, state, now);
}
