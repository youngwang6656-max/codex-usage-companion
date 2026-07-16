namespace CodexUsageCompanion.Windows;

public interface IWindowFrameClock : IDisposable
{
    event Action? Tick;

    bool IsHighResolution { get; }

    void Schedule(TimeSpan delay);

    void Cancel();
}

public sealed class LatestWindowFramePump<T> : IDisposable
{
    private readonly IWindowFrameClock _clock;
    private readonly object _gate = new();
    private readonly List<PendingFrame> _pending = [];
    private bool _scheduled;
    private bool _processing;
    private bool _disposed;

    public LatestWindowFramePump(IWindowFrameClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _clock.Tick += OnTick;
    }

    public event Action<T>? FrameReady;

    public void Offer(T value, TimeSpan delay, bool coalesce = true)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var frame = new PendingFrame(
                value,
                delay < TimeSpan.Zero ? TimeSpan.Zero : delay,
                coalesce);
            if (coalesce && _pending.Count > 0 && _pending[^1].Coalescible)
            {
                _pending[^1] = frame;
            }
            else
            {
                _pending.Add(frame);
            }

            if (_processing)
            {
                return;
            }

            if (!_scheduled)
            {
                _scheduled = true;
                _clock.Schedule(_pending[0].Delay);
                return;
            }

            if (frame.Delay == TimeSpan.Zero)
            {
                _clock.Schedule(TimeSpan.Zero);
            }
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            if (_disposed || _pending.Count == 0 || _processing)
            {
                return;
            }

            PendingFrame first = _pending[0];
            _pending[0] = first with { Delay = TimeSpan.Zero };
            _scheduled = true;
            _clock.Schedule(TimeSpan.Zero);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pending.Clear();
            _clock.Cancel();
            _clock.Tick -= OnTick;
        }

        _clock.Dispose();
    }

    private void OnTick()
    {
        PendingFrame frame;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _scheduled = false;
            if (_processing || _pending.Count == 0)
            {
                return;
            }

            frame = _pending[0];
            _pending.RemoveAt(0);
            _processing = true;
        }

        try
        {
            FrameReady?.Invoke(frame.Value);
        }
        finally
        {
            lock (_gate)
            {
                _processing = false;
                if (!_disposed && _pending.Count > 0)
                {
                    _scheduled = true;
                    _clock.Schedule(_pending[0].Delay);
                }
            }
        }
    }

    private sealed record PendingFrame(T Value, TimeSpan Delay, bool Coalescible);
}
