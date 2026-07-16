using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CodexUsageCompanion.Windows;

public sealed class Win32WindowSnapshotSource : IWindowSnapshotSource
{
    private const uint EventObjectDestroy = 0x8001;
    private const uint EventObjectShow = 0x8002;
    private const uint EventObjectHide = 0x8003;
    private const uint EventObjectLocationChange = 0x800B;
    private const uint EventSystemForeground = 0x0003;
    private const uint EventSystemMoveSizeStart = 0x000A;
    private const uint EventSystemMoveSizeEnd = 0x000B;
    private const uint WineventOutOfContext = 0x0000;
    private const uint WineventSkipOwnProcess = 0x0002;
    private const int ObjidWindow = 0;
    private const int DwmwaExtendedFrameBounds = 9;
    private const int DwmwaCloaked = 14;
    private const uint MonitorDefaultToNearest = 2;
    private const int GwlExstyle = -20;
    private const long WsExToolwindow = 0x00000080L;

    private readonly WinEventDelegate _winEventCallback;
    private readonly EnumWindowsDelegate _enumWindowsCallback;
    private readonly CodexProcessCatalog _processCatalog;
    private readonly AdaptiveFollowRateController _followRate = new();
    private readonly MovementActivityDetector _movementActivity = new();
    private readonly Process _currentProcess = Process.GetCurrentProcess();
    private readonly object _gate = new();
    private readonly List<nint> _globalHooks = [];
    private readonly List<nint> _processHooks = [];
    private LatestWindowFramePump<WindowChangeSignal>? _framePump;
    private Timer? _pollTimer;
    private Timer? _performanceTimer;
    private Timer? _movementInactivityTimer;
    private bool _started;
    private bool _disposed;
    private uint _hookedProcessId;
    private long _scheduledAtTimestamp;
    private TimeSpan _scheduledInterval;
    private TimeSpan _lastCpuTime;
    private long _lastCpuSampleTimestamp;
    private int _scheduledFrameCount;
    private int _lateFrameCount;
    private bool _isMovingSizing;
    private nint _lastMovementWindow;

    public Win32WindowSnapshotSource()
        : this(new CodexProcessCatalog(new SystemProcessSnapshotProvider()))
    {
    }

    public Win32WindowSnapshotSource(CodexProcessCatalog processCatalog)
    {
        _processCatalog = processCatalog ?? throw new ArgumentNullException(nameof(processCatalog));
        _winEventCallback = OnWinEvent;
        _enumWindowsCallback = OnEnumWindow;
    }

    public event EventHandler<WindowChangeSignal>? Changed;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        _started = true;
        _framePump = new LatestWindowFramePump<WindowChangeSignal>(
            new WaitableTimerWindowFrameClock());
        _framePump.FrameReady += RaiseChanged;
        _performanceTimer = new Timer(
            _ => SampleFollowPerformance(),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        _movementInactivityTimer = new Timer(
            _ => EndInferredMovementAfterInactivity(),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        _pollTimer = new Timer(
            _ => ScheduleChanged(new WindowChangeSignal(0, WindowChangeKind.Poll, 0), immediate: false),
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
        foreach (WindowEventSubscription subscription in WindowEventSubscriptionPlanner.ForStartup())
        {
            AddHook(subscription, _globalHooks);
        }
    }

    public IReadOnlyList<CodexWindowCandidate> GetCandidates()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var candidates = new List<CodexWindowCandidate>();
        _enumerationDestination = candidates;
        _knownCodexProcesses = _processCatalog.GetKnownProcesses();
        try
        {
            _ = EnumWindows(_enumWindowsCallback, 0);
        }
        finally
        {
            _enumerationDestination = null;
            _knownCodexProcesses = null;
        }

        if (candidates.Count == 0)
        {
            _processCatalog.Invalidate();
        }

        EnsureProcessHooks(candidates.Select(candidate => candidate.ProcessId).FirstOrDefault());
        return candidates;
    }

    public bool TryGetCandidate(nint handle, out CodexWindowCandidate candidate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        candidate = default!;
        if (handle == 0 || !IsWindow(handle))
        {
            return false;
        }

        _ = GetWindowThreadProcessId(handle, out uint processId);
        IReadOnlyDictionary<uint, string> knownProcesses = _processCatalog.GetKnownProcesses();
        if (!knownProcesses.TryGetValue(processId, out string? executablePath))
        {
            _processCatalog.Invalidate();
            knownProcesses = _processCatalog.GetKnownProcesses();
            if (!knownProcesses.TryGetValue(processId, out executablePath))
            {
                return false;
            }
        }

        candidate = CreateCandidate(handle, processId, executablePath, includeTitle: false);
        return true;
    }

    public nint GetForegroundWindow() => NativeGetForegroundWindow();

    public PixelRect GetWorkArea(nint windowHandle)
    {
        nint monitor = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return monitor != 0 && GetMonitorInfo(monitor, ref info)
            ? ToPixelRect(info.WorkArea)
            : new PixelRect(
                0,
                0,
                GetSystemMetrics(0),
                GetSystemMetrics(1));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_gate)
        {
            _pollTimer?.Dispose();
            _performanceTimer?.Dispose();
            _movementInactivityTimer?.Dispose();
            _pollTimer = null;
            _performanceTimer = null;
            _movementInactivityTimer = null;
        }

        LatestWindowFramePump<WindowChangeSignal>? framePump = _framePump;
        _framePump = null;
        if (framePump is not null)
        {
            framePump.FrameReady -= RaiseChanged;
            framePump.Dispose();
        }

        foreach (nint hook in _globalHooks.Concat(_processHooks))
        {
            _ = UnhookWinEvent(hook);
        }

        _globalHooks.Clear();
        _processHooks.Clear();
        _currentProcess.Dispose();
    }

    [ThreadStatic]
    private static List<CodexWindowCandidate>? _enumerationDestination;

    [ThreadStatic]
    private static IReadOnlyDictionary<uint, string>? _knownCodexProcesses;

    private void AddHook(WindowEventSubscription subscription, ICollection<nint> destination)
    {
        nint hook = SetWinEventHook(
            subscription.EventMin,
            subscription.EventMax,
            0,
            _winEventCallback,
            subscription.ProcessId,
            0,
            WineventOutOfContext | WineventSkipOwnProcess);
        if (hook != 0)
        {
            destination.Add(hook);
        }
    }

    private void EnsureProcessHooks(uint processId)
    {
        lock (_gate)
        {
            if (_hookedProcessId == processId)
            {
                return;
            }

            foreach (nint hook in _processHooks)
            {
                _ = UnhookWinEvent(hook);
            }

            _processHooks.Clear();
            _hookedProcessId = processId;
            if (processId == 0)
            {
                return;
            }

            foreach (WindowEventSubscription subscription in WindowEventSubscriptionPlanner.ForCodexProcess(processId))
            {
                AddHook(subscription, _processHooks);
            }
        }
    }

    private void OnWinEvent(
        nint hook,
        uint eventType,
        nint windowHandle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (_disposed || (eventType >= EventObjectDestroy && objectId != ObjidWindow))
        {
            return;
        }

        if (eventType >= EventObjectDestroy
            && eventType is not EventObjectDestroy
                and not EventObjectShow
                and not EventObjectHide
                and not EventObjectLocationChange)
        {
            return;
        }

        if (eventType == EventSystemForeground)
        {
            _processCatalog.Invalidate();
        }

        WindowChangeKind kind = eventType switch
        {
            EventSystemForeground => WindowChangeKind.Foreground,
            EventSystemMoveSizeStart => WindowChangeKind.MoveSizeStarted,
            EventSystemMoveSizeEnd => WindowChangeKind.MoveSizeEnded,
            EventObjectLocationChange => WindowChangeKind.Location,
            EventObjectDestroy => WindowChangeKind.Destroyed,
            EventObjectShow or EventObjectHide => WindowChangeKind.Visibility,
            _ => WindowChangeKind.Visibility,
        };
        long nowMilliseconds = Environment.TickCount64;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (kind == WindowChangeKind.MoveSizeStarted)
            {
                _lastMovementWindow = windowHandle;
                if (_movementActivity.OnOfficialMoveStarted(nowMilliseconds))
                {
                    BeginMoveSizeSamplingLocked();
                }
            }
            else if (kind == WindowChangeKind.MoveSizeEnded)
            {
                _lastMovementWindow = windowHandle;
                if (_movementActivity.OnOfficialMoveEnded(nowMilliseconds))
                {
                    EndMoveSizeSamplingLocked();
                }
            }
            else if (kind == WindowChangeKind.Location)
            {
                _lastMovementWindow = windowHandle;
                if (_movementActivity.OnLocation(nowMilliseconds))
                {
                    BeginMoveSizeSamplingLocked();
                }

                _movementInactivityTimer?.Change(
                    TimeSpan.FromMilliseconds(150),
                    Timeout.InfiniteTimeSpan);
            }
        }

        bool immediate = kind is not WindowChangeKind.Location;
        ScheduleChanged(new WindowChangeSignal(windowHandle, kind, eventTime), immediate);
    }

    private void ScheduleChanged(WindowChangeSignal signal, bool immediate)
    {
        LatestWindowFramePump<WindowChangeSignal>? framePump;
        TimeSpan interval;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            interval = immediate ? TimeSpan.Zero : _followRate.FrameInterval;
            _scheduledInterval = interval;
            _scheduledAtTimestamp = Stopwatch.GetTimestamp();
            framePump = _framePump;
        }

        bool coalesce = signal.Kind is WindowChangeKind.Location or WindowChangeKind.Poll;
        framePump?.Offer(signal, interval, coalesce);
    }

    private void RaiseChanged(WindowChangeSignal signal)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_scheduledAtTimestamp != 0 && _scheduledInterval > TimeSpan.Zero)
            {
                TimeSpan elapsed = Stopwatch.GetElapsedTime(_scheduledAtTimestamp);
                if (elapsed > _scheduledInterval + TimeSpan.FromTicks(_scheduledInterval.Ticks / 4))
                {
                    _lateFrameCount++;
                }
            }

            _scheduledFrameCount++;
            _scheduledAtTimestamp = 0;
        }

        Changed?.Invoke(this, signal);
    }

    private void EndInferredMovementAfterInactivity()
    {
        nint handle;
        lock (_gate)
        {
            if (_disposed
                || !_movementActivity.TryEndForInactivity(Environment.TickCount64))
            {
                return;
            }

            handle = _lastMovementWindow;
            EndMoveSizeSamplingLocked();
        }

        ScheduleChanged(
            new WindowChangeSignal(handle, WindowChangeKind.MoveSizeEnded, 0),
            immediate: true);
    }

    private void BeginMoveSizeSamplingLocked()
    {
        _isMovingSizing = true;
        _followRate.OnMoveSizeStarted();
        _currentProcess.Refresh();
        _lastCpuTime = _currentProcess.TotalProcessorTime;
        _lastCpuSampleTimestamp = Stopwatch.GetTimestamp();
        _scheduledFrameCount = 0;
        _lateFrameCount = 0;
        _performanceTimer?.Change(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    private void EndMoveSizeSamplingLocked()
    {
        _isMovingSizing = false;
        _followRate.OnMoveSizeEnded();
        _scheduledFrameCount = 0;
        _lateFrameCount = 0;
        _performanceTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _movementInactivityTimer?.Change(
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    private void SampleFollowPerformance()
    {
        lock (_gate)
        {
            if (_disposed || !_isMovingSizing || _lastCpuSampleTimestamp == 0)
            {
                return;
            }

            _currentProcess.Refresh();
            long now = Stopwatch.GetTimestamp();
            TimeSpan wallTime = Stopwatch.GetElapsedTime(_lastCpuSampleTimestamp, now);
            TimeSpan cpuTime = _currentProcess.TotalProcessorTime;
            double cpuPercent = wallTime.TotalMilliseconds <= 0
                ? 0
                : (cpuTime - _lastCpuTime).TotalMilliseconds
                    / wallTime.TotalMilliseconds
                    / Math.Max(1, Environment.ProcessorCount)
                    * 100;
            double missedRatio = _scheduledFrameCount == 0
                ? 0
                : (double)_lateFrameCount / _scheduledFrameCount;

            _followRate.Report(cpuPercent, missedRatio);
            _lastCpuTime = cpuTime;
            _lastCpuSampleTimestamp = now;
            _scheduledFrameCount = 0;
            _lateFrameCount = 0;
        }
    }

    private bool OnEnumWindow(nint windowHandle, nint parameter)
    {
        List<CodexWindowCandidate>? destination = _enumerationDestination;
        if (destination is null)
        {
            return false;
        }

        uint processId;
        _ = GetWindowThreadProcessId(windowHandle, out processId);
        if (_knownCodexProcesses is null
            || !_knownCodexProcesses.TryGetValue(processId, out string? executablePath))
        {
            return true;
        }

        bool isVisible = IsWindowVisible(windowHandle);
        if (!isVisible)
        {
            return true;
        }

        destination.Add(CreateCandidate(windowHandle, processId, executablePath, includeTitle: true));
        return true;
    }

    private static CodexWindowCandidate CreateCandidate(
        nint windowHandle,
        uint processId,
        string executablePath,
        bool includeTitle) =>
        new(
            windowHandle,
            executablePath,
            includeTitle ? GetWindowTitle(windowHandle) : string.Empty,
            GetExtendedFrameBounds(windowHandle),
            GetWindowDpi(windowHandle),
            IsWindowVisible(windowHandle),
            IsIconic(windowHandle),
            IsCloaked(windowHandle),
            processId,
            IsZoomed(windowHandle),
            (GetWindowLongPtr(windowHandle, GwlExstyle).ToInt64() & WsExToolwindow) != 0);

    private static string GetWindowTitle(nint windowHandle)
    {
        int length = GetWindowTextLength(windowHandle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(windowHandle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static PixelRect GetExtendedFrameBounds(nint windowHandle)
    {
        if (DwmGetWindowAttribute(
                windowHandle,
                DwmwaExtendedFrameBounds,
                out NativeRect bounds,
                Marshal.SizeOf<NativeRect>()) != 0)
        {
            _ = GetWindowRect(windowHandle, out bounds);
        }

        return ToPixelRect(bounds);
    }

    private static bool IsCloaked(nint windowHandle) =>
        DwmGetWindowAttribute(
            windowHandle,
            DwmwaCloaked,
            out int cloaked,
            sizeof(int)) == 0 && cloaked != 0;

    private static uint GetWindowDpi(nint windowHandle)
    {
        uint dpi = GetDpiForWindow(windowHandle);
        return dpi == 0 ? 96u : dpi;
    }

    private static PixelRect ToPixelRect(NativeRect rect) =>
        new(rect.Left, rect.Top, rect.Right, rect.Bottom);

    private delegate bool EnumWindowsDelegate(nint windowHandle, nint parameter);

    private delegate void WinEventDelegate(
        nint hook,
        uint eventType,
        nint windowHandle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsDelegate callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint eventHookModule,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern nint NativeGetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(nint windowHandle);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint windowHandle, int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint windowHandle, out NativeRect bounds);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint windowHandle, StringBuilder text, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint windowHandle,
        int attribute,
        out NativeRect value,
        int valueSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint windowHandle,
        int attribute,
        out int value,
        int valueSize);
}
