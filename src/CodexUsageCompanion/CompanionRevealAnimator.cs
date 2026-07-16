using System.Diagnostics;
using CodexUsageCompanion.Windows;

namespace CodexUsageCompanion;

internal readonly record struct CompanionRevealAnimationResult(
    bool Completed,
    double Progress);

internal interface ICompanionRevealAnimator : IDisposable
{
    double Progress { get; }

    bool IsRunning { get; }

    void SnapTo(double progress);

    Task<CompanionRevealAnimationResult> AnimateToAsync(
        double targetProgress,
        TimeSpan fullDuration,
        Func<double, bool> applyFrame,
        CancellationToken cancellationToken);

    void Cancel();
}

internal sealed class CompanionRevealAnimator : ICompanionRevealAnimator
{
    private static readonly TimeSpan FrameInterval = TimeSpan.FromSeconds(1d / 60d);
    private readonly object _gate = new();
    private CancellationTokenSource? _runCancellation;
    private double _progress = 1;
    private int _generation;
    private bool _disposed;

    public double Progress => Interlocked.CompareExchange(ref _progress, 0, 0);

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _runCancellation is not null;
            }
        }
    }

    public Task<CompanionRevealAnimationResult> AnimateToAsync(
        double targetProgress,
        TimeSpan fullDuration,
        Func<double, bool> applyFrame,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(applyFrame);
        CancellationTokenSource? previous;
        CancellationTokenSource linked;
        double start;
        int generation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            previous = _runCancellation;
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _runCancellation = linked;
            start = Progress;
            generation = ++_generation;
        }

        previous?.Cancel();

        double target = Math.Clamp(targetProgress, 0, 1);
        TimeSpan duration = CompanionAnimationMath.ScaleDuration(
            fullDuration,
            start,
            target);
        return RunAsync(
            generation,
            start,
            target,
            duration,
            applyFrame,
            linked);
    }

    public void Cancel()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            cancellation = _runCancellation;
            _runCancellation = null;
            _generation++;
        }

        cancellation?.Cancel();
    }

    public void SnapTo(double progress)
    {
        Cancel();
        Interlocked.Exchange(ref _progress, Math.Clamp(progress, 0, 1));
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
        }

        Cancel();
    }

    private async Task<CompanionRevealAnimationResult> RunAsync(
        int generation,
        double start,
        double target,
        TimeSpan duration,
        Func<double, bool> applyFrame,
        CancellationTokenSource cancellation)
    {
        try
        {
            if (duration <= TimeSpan.Zero)
            {
                Interlocked.Exchange(ref _progress, target);
                return new CompanionRevealAnimationResult(
                    applyFrame(target),
                    target);
            }

            using var clock = new WaitableTimerWindowFrameClock();
            var stopwatch = Stopwatch.StartNew();
            while (true)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                double linear = Math.Clamp(
                    stopwatch.Elapsed.TotalMilliseconds / duration.TotalMilliseconds,
                    0,
                    1);
                double eased = CompanionAnimationMath.CubicEaseInOut(linear);
                double progress = start + ((target - start) * eased);
                if (!IsCurrentGeneration(generation))
                {
                    return new CompanionRevealAnimationResult(false, Progress);
                }

                Interlocked.Exchange(ref _progress, progress);
                if (!applyFrame(progress))
                {
                    return new CompanionRevealAnimationResult(false, progress);
                }

                if (linear >= 1)
                {
                    Interlocked.Exchange(ref _progress, target);
                    return new CompanionRevealAnimationResult(true, target);
                }

                await WaitForTickAsync(clock, FrameInterval, cancellation.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return new CompanionRevealAnimationResult(false, Progress);
        }
        finally
        {
            lock (_gate)
            {
                if (_runCancellation == cancellation)
                {
                    _runCancellation = null;
                }
            }

            cancellation.Dispose();
        }
    }

    private bool IsCurrentGeneration(int generation)
    {
        lock (_gate)
        {
            return !_disposed && generation == _generation;
        }
    }

    private static async Task WaitForTickAsync(
        IWindowFrameClock clock,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void Complete() => completion.TrySetResult();
        clock.Tick += Complete;
        try
        {
            clock.Schedule(delay);
            await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            clock.Tick -= Complete;
            clock.Cancel();
        }
    }
}
