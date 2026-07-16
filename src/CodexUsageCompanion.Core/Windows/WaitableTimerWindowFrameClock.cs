using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace CodexUsageCompanion.Windows;

public sealed class WaitableTimerWindowFrameClock : IWindowFrameClock
{
    private const uint CreateWaitableTimerHighResolution = 0x00000002;
    private const uint TimerAllAccess = 0x001F0003;
    private readonly object _gate = new();
    private EventWaitHandle? _waitHandle;
    private RegisteredWaitHandle? _registeredWait;
    private Timer? _fallbackTimer;
    private bool _disposed;

    public WaitableTimerWindowFrameClock(bool forceFallback = false)
    {
        if (!forceFallback && TryCreateHighResolutionTimer())
        {
            IsHighResolution = true;
            return;
        }

        _fallbackTimer = new Timer(
            static state => ((WaitableTimerWindowFrameClock)state!).RaiseTick(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    public event Action? Tick;

    public bool IsHighResolution { get; private set; }

    public void Schedule(TimeSpan delay)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            TimeSpan due = delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
            if (IsHighResolution && _waitHandle is not null)
            {
                long dueTime = -Math.Max(1, due.Ticks);
                if (SetWaitableTimerEx(
                        _waitHandle.SafeWaitHandle,
                        ref dueTime,
                        0,
                        0,
                        0,
                        0,
                        0))
                {
                    return;
                }

                SwitchToFallbackLocked();
            }

            _fallbackTimer?.Change(due, Timeout.InfiniteTimeSpan);
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (IsHighResolution && _waitHandle is not null)
            {
                _ = CancelWaitableTimer(_waitHandle.SafeWaitHandle);
            }

            _fallbackTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
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
            _fallbackTimer?.Dispose();
            _fallbackTimer = null;
            _registeredWait?.Unregister(null);
            _registeredWait = null;
            _waitHandle?.Dispose();
            _waitHandle = null;
            IsHighResolution = false;
        }
    }

    private bool TryCreateHighResolutionTimer()
    {
        nint timer = CreateWaitableTimerEx(
            0,
            null,
            CreateWaitableTimerHighResolution,
            TimerAllAccess);
        if (timer == 0)
        {
            return false;
        }

        var waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
        waitHandle.SafeWaitHandle = new SafeWaitHandle(timer, ownsHandle: true);
        _waitHandle = waitHandle;
        _registeredWait = ThreadPool.RegisterWaitForSingleObject(
            waitHandle,
            static (state, _) => ((WaitableTimerWindowFrameClock)state!).RaiseTick(),
            this,
            Timeout.Infinite,
            executeOnlyOnce: false);
        return true;
    }

    private void SwitchToFallbackLocked()
    {
        _registeredWait?.Unregister(null);
        _registeredWait = null;
        _waitHandle?.Dispose();
        _waitHandle = null;
        IsHighResolution = false;
        _fallbackTimer ??= new Timer(
            static state => ((WaitableTimerWindowFrameClock)state!).RaiseTick(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    private void RaiseTick()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
        }

        Tick?.Invoke();
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW", CharSet = CharSet.Unicode)]
    private static extern nint CreateWaitableTimerEx(
        nint timerAttributes,
        string? timerName,
        uint flags,
        uint desiredAccess);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimerEx(
        SafeWaitHandle timer,
        ref long dueTime,
        int period,
        nint completionRoutine,
        nint completionArgument,
        nint wakeContext,
        uint tolerableDelay);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelWaitableTimer(SafeWaitHandle timer);
}
