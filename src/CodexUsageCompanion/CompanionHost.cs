using System.IO;
using System.Windows;
using System.Windows.Threading;
using CodexUsageCompanion.Protocol;
using CodexUsageCompanion.Usage;
using CodexUsageCompanion.Windows;

namespace CodexUsageCompanion;

public sealed class CompanionHost : IAsyncDisposable
{
    private static readonly TimeSpan AuthenticationRetryInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ExpandDuration = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan CollapseDuration = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan FullscreenExpandDuration = TimeSpan.FromMilliseconds(220);
    private readonly ICodexWindowTracker _windowTracker;
    private readonly ManagedWindowPlacementCoordinator _placementCoordinator;
    private readonly ICompanionSettingsStore _settingsStore;
    private readonly IComposerAnchorLocator _anchorLocator;
    private readonly ICompanionRevealAnimator _revealAnimator;
    private readonly ICompanionRevealAnimator _anchorCalibrationAnimator =
        new CompanionRevealAnimator();
    private readonly LatestValueDispatchQueue<CodexWindowState?> _windowStateQueue = new();
    private readonly SemaphoreSlim _dataLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Timer _authenticationRetryTimer;
    private CancellationTokenSource? _integrationAttemptCancellation;
    private Task<ManagedIntegrationResult>? _integrationAttemptTask;
    private nint _integrationAttemptHandle;
    private PixelRect _integrationAttemptTarget;
    private UsageWindow? _window;
    private CollapsedToggleWindow? _toggleWindow;
    private ICodexUsageClient? _usageClient;
    private CodexWindowState? _lastWindowState;
    private UsageSnapshot _lastSnapshot = new(
        null,
        null,
        null,
        null,
        SyncState.Syncing,
        DateTimeOffset.Now);
    private CompanionUserSettings _settings = CompanionUserSettings.Default;
    private CompanionVisibilityStateMachine _visibility = new(
        CompanionVisibilityPreference.Expanded);
    private bool _disposed;
    private int _fastFollowEnabled;
    private int _visibilityGeneration;
    private int _dynamicFullscreenReveal;
    private int _moveSizeActive;
    private int _anchorCalibrationGeneration;
    private PixelRect _lastToggleBounds;
    private nint _capturedAnchorHandle;

    private CompanionHost(
        ICodexWindowTracker windowTracker,
        ManagedWindowPlacementCoordinator placementCoordinator,
        ICompanionSettingsStore settingsStore,
        IComposerAnchorLocator anchorLocator,
        ICompanionRevealAnimator revealAnimator)
    {
        _windowTracker = windowTracker;
        _placementCoordinator = placementCoordinator;
        _settingsStore = settingsStore;
        _anchorLocator = anchorLocator;
        _revealAnimator = revealAnimator;
        _authenticationRetryTimer = new Timer(
            _ => ScheduleAuthenticationRetry(),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    public static CompanionHost CreateDefault()
    {
        var source = new Win32WindowSnapshotSource();
        return new CompanionHost(
            new CodexWindowTracker(source),
            new ManagedWindowPlacementCoordinator(new Win32ManagedWindowNative()),
            JsonCompanionSettingsStore.CreateDefault(),
            new DeterministicComposerAnchorLocator(),
            new CompanionRevealAnimator());
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _settings = LoadSettings();
        _visibility = new CompanionVisibilityStateMachine(_settings.Visibility);
        _revealAnimator.SnapTo(
            _settings.Visibility == CompanionVisibilityPreference.Expanded ? 1 : 0);
        _anchorLocator.AnchorUpdated += OnAnchorUpdated;
        _windowTracker.WindowChanged += OnWindowChanged;
        _windowTracker.Start();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelIntegrationAttempt();
        _revealAnimator.Cancel();
        _lifetime.Cancel();
        _authenticationRetryTimer.Dispose();
        _anchorLocator.AnchorUpdated -= OnAnchorUpdated;
        _windowTracker.WindowChanged -= OnWindowChanged;
        _windowTracker.Dispose();

        await _placementCoordinator.RestoreAllOwnedAsync(CancellationToken.None);
        if (_lastWindowState is { } lastState)
        {
            _placementCoordinator.Forget(lastState.Handle);
        }

        UsageWindow? window = _window;
        _window = null;
        if (window is not null)
        {
            window.VisibilityToggleRequested -= OnVisibilityToggleRequested;
            window.RefreshRequested -= OnRefreshRequested;
            if (window.Dispatcher.CheckAccess())
            {
                window.Close();
            }
            else
            {
                await window.Dispatcher.InvokeAsync(window.Close, DispatcherPriority.Send);
            }
        }

        CollapsedToggleWindow? toggleWindow = _toggleWindow;
        _toggleWindow = null;
        if (toggleWindow is not null)
        {
            toggleWindow.VisibilityToggleRequested -= OnVisibilityToggleRequested;
            if (toggleWindow.Dispatcher.CheckAccess())
            {
                toggleWindow.Close();
            }
            else
            {
                await toggleWindow.Dispatcher.InvokeAsync(
                    toggleWindow.Close,
                    DispatcherPriority.Send);
            }
        }

        await _dataLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisposeUsageClientCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _dataLock.Release();
        }

        _dataLock.Dispose();
        _anchorCalibrationAnimator.Dispose();
        _anchorLocator.Dispose();
        _revealAnimator.Dispose();
        _lifetime.Dispose();
    }

    private void OnWindowChanged(object? sender, CodexWindowState? state)
    {
        if (_disposed)
        {
            return;
        }

        if (state?.ChangeKind == WindowChangeKind.MoveSizeStarted)
        {
            Volatile.Write(ref _moveSizeActive, 1);
            _anchorCalibrationAnimator.Cancel();
            Interlocked.Increment(ref _anchorCalibrationGeneration);
        }
        else if (state?.ChangeKind == WindowChangeKind.MoveSizeEnded)
        {
            Volatile.Write(ref _moveSizeActive, 0);
        }

        if (state is
            {
                IsVisible: true,
                IsMinimized: false,
            }
            && state.ChangeKind is WindowChangeKind.Location or WindowChangeKind.MoveSizeEnded
            && Volatile.Read(ref _fastFollowEnabled) != 0)
        {
            if (_visibility.State == CompanionVisibilityState.Collapsed)
            {
                ComposerAnchor anchor = _anchorLocator.Locate(state, allowCapture: false);
                _toggleWindow?.TryFastReposition(anchor.ButtonBounds);
            }
            else if (_visibility.State is CompanionVisibilityState.Expanding
                     or CompanionVisibilityState.Collapsing)
            {
                _ = ApplyRevealFrame(_revealAnimator.Progress, state);
            }
            else
            {
                _window?.TryFastReposition(
                    CompanionLayoutPlanner.ForNormalWindow(state).Companion,
                    state.Handle);
            }
        }

        bool coalesce = state is not null
            && state.ChangeKind is WindowChangeKind.Location or WindowChangeKind.Poll;
        if (_windowStateQueue.Offer(state, coalesce))
        {
            _ = Application.Current.Dispatcher.InvokeAsync(
                DrainWindowStateQueue,
                DispatcherPriority.Render);
        }
    }

    private async void DrainWindowStateQueue()
    {
        try
        {
            while (_windowStateQueue.TryTake(out CodexWindowState? state))
            {
                await ApplyWindowStateAsync(state);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async Task ApplyWindowStateAsync(CodexWindowState? state)
    {
        if (_disposed)
        {
            return;
        }

        CodexWindowState? previous = _lastWindowState;
        if (previous is not null && (state is null || previous.Handle != state.Handle))
        {
            CancelIntegrationAttempt();
            _revealAnimator.Cancel();
            Interlocked.Increment(ref _visibilityGeneration);
            await _placementCoordinator.RestoreIfOwnedAsync(
                previous.Handle,
                _lifetime.Token);
            _placementCoordinator.Forget(previous.Handle);
            _anchorLocator.Forget(previous.Handle);
            _capturedAnchorHandle = 0;
            _lastToggleBounds = default;
        }

        _lastWindowState = state;

        if (state is null)
        {
            Volatile.Write(ref _fastFollowEnabled, 0);
            CancelIntegrationAttempt();
            HideCompanionWindows();
            _ = DisposeUsageClientSafelyAsync();
            return;
        }

        bool wasHidden = previous is null
            || previous.IsMinimized
            || !previous.IsVisible;

        if (!state.IsVisible || state.IsMinimized)
        {
            Volatile.Write(ref _fastFollowEnabled, 0);
            CancelIntegrationAttempt();
            HideCompanionWindows();
            return;
        }

        _window ??= CreateWindow();
        _toggleWindow ??= CreateToggleWindow();

        if (state.ChangeKind == WindowChangeKind.MoveSizeStarted)
        {
            CancelIntegrationAttempt();
            _revealAnimator.Cancel();
            Interlocked.Increment(ref _visibilityGeneration);
            ManagedRestoreResult restore = await _placementCoordinator.RestoreForUserActionAsync(
                state.Handle,
                _lifetime.Token);
            CodexWindowState positionedState = restore.Attempts > 0
                ? state with
                {
                    Bounds = restore.ActualBounds,
                    IsMaximized = !restore.Succeeded && state.IsMaximized,
                }
                : state;
            Volatile.Write(ref _fastFollowEnabled, 1);
            ShowStableVisibility(positionedState, isFullscreen: false);
            return;
        }

        PixelRect workArea = _windowTracker.GetWorkArea(state.Handle);
        if (_placementCoordinator.TryGetOwned(
                state.Handle,
                out CompanionLayoutMode ownedMode,
                out PixelRect ownedTarget))
        {
            if (IsWithinOnePixel(state.Bounds, ownedTarget))
            {
                if (_revealAnimator.IsRunning
                    && _visibility.State is CompanionVisibilityState.Expanding
                        or CompanionVisibilityState.Collapsing)
                {
                    _ = ApplyRevealFrame(_revealAnimator.Progress, state);
                }
                else if (_visibility.State == CompanionVisibilityState.DeferredFullscreenExpand)
                {
                    HideCompanionWindows();
                }
                else if (_settings.Visibility == CompanionVisibilityPreference.Collapsed)
                {
                    Volatile.Write(ref _fastFollowEnabled, 1);
                    ShowCollapsed(state, allowCapture: ShouldCaptureAnchor(state));
                }
                else
                {
                    Volatile.Write(ref _fastFollowEnabled, 0);
                    ShowIntegrated(state, workArea, ownedMode);
                }

                _ = EnsureUsageForVisibleWindowAsync(
                    refreshExisting: wasHidden || state.ChangeKind == WindowChangeKind.Foreground);
                return;
            }

            ManagedRestoreResult restore = await _placementCoordinator.RestoreForUserActionAsync(
                state.Handle,
                _lifetime.Token);
            CodexWindowState restoredState = state with
            {
                Bounds = restore.ActualBounds,
                IsMaximized = !restore.Succeeded && state.IsMaximized,
            };
            Volatile.Write(ref _fastFollowEnabled, 1);
            ShowStableVisibility(restoredState, isFullscreen: false);
            return;
        }

        bool matchesActiveIntegrationAttempt =
            _integrationAttemptTask is not null
            && _integrationAttemptHandle == state.Handle
            && IsWithinOnePixel(state.Bounds, _integrationAttemptTarget);
        bool fullscreen = state.IsMaximized
            || FillsWorkArea(state.Bounds, workArea)
            || matchesActiveIntegrationAttempt;
        if (fullscreen)
        {
            if (Volatile.Read(ref _dynamicFullscreenReveal) != 0)
            {
                _ = EnsureUsageForVisibleWindowAsync(
                    refreshExisting: wasHidden || state.ChangeKind == WindowChangeKind.Foreground);
                return;
            }

            if (_visibility.State == CompanionVisibilityState.DeferredFullscreenExpand)
            {
                Volatile.Write(ref _fastFollowEnabled, 0);
                HideCompanionWindows();
                _ = EnsureUsageForVisibleWindowAsync(
                    refreshExisting: wasHidden || state.ChangeKind == WindowChangeKind.Foreground);
                return;
            }

            if (_settings.Visibility == CompanionVisibilityPreference.Collapsed)
            {
                CancelIntegrationAttempt();
                Volatile.Write(ref _fastFollowEnabled, 1);
                ShowCollapsed(state, allowCapture: ShouldCaptureAnchor(state));
                _ = EnsureUsageForVisibleWindowAsync(
                    refreshExisting: wasHidden || state.ChangeKind == WindowChangeKind.Foreground);
                return;
            }

            Volatile.Write(ref _fastFollowEnabled, 0);
            ShowFullscreenOverlay(state, workArea);
            CompanionLayoutDecision requested = CompanionLayoutPlanner.CreateIntegrated(
                workArea,
                state.Dpi,
                CompanionLayoutMode.NativeIntegratedFullscreen);

            try
            {
                ManagedIntegrationResult result = await GetOrStartIntegrationAttempt(
                    state.Handle,
                    requested.CodexTarget!.Value,
                    _lifetime.Token);

                CodexWindowState? current = _windowTracker.Current;
                if (current is null
                    || current.Handle != state.Handle
                    || current.IsMinimized
                    || !current.IsVisible
                    || current.ChangeKind == WindowChangeKind.MoveSizeStarted)
                {
                    await _placementCoordinator.RestoreIfOwnedAsync(
                        state.Handle,
                        _lifetime.Token);
                    return;
                }

                if (result.Mode is CompanionLayoutMode.NativeIntegratedFullscreen
                    or CompanionLayoutMode.ManagedIntegratedFullscreen)
                {
                    ShowIntegrated(current, workArea, result.Mode);
                }
                else
                {
                    ShowFullscreenOverlay(current, workArea);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
        else
        {
            CancelIntegrationAttempt();
            _placementCoordinator.ClearIntegrationFailure(state.Handle);
            _placementCoordinator.RecordNormalBounds(state.Handle, state.Bounds, state.Dpi);
            Volatile.Write(ref _fastFollowEnabled, 1);
            bool wasDeferred = _visibility.State
                == CompanionVisibilityState.DeferredFullscreenExpand;
            if (wasDeferred)
            {
                _ = _visibility.Windowed();
                _revealAnimator.SnapTo(0);
            }

            if (_revealAnimator.IsRunning
                && _visibility.State is CompanionVisibilityState.Expanding
                    or CompanionVisibilityState.Collapsing)
            {
                _ = ApplyRevealFrame(_revealAnimator.Progress, state);
            }
            else if (wasDeferred)
            {
                int generation = Interlocked.Increment(ref _visibilityGeneration);
                await AnimateVisibilityAsync(state, expand: true, generation);
            }
            else
            {
                ShowStableVisibility(state, isFullscreen: false);
            }
        }

        _ = EnsureUsageForVisibleWindowAsync(
            refreshExisting: wasHidden || state.ChangeKind == WindowChangeKind.Foreground);
    }

    private void ShowIntegrated(
        CodexWindowState state,
        PixelRect workArea,
        CompanionLayoutMode mode)
    {
        CompanionLayoutDecision decision = CompanionLayoutPlanner.CreateIntegrated(
            workArea,
            state.Dpi,
            mode);
        _toggleWindow?.Hide();
        _window?.SetVisibilityButtonVisible(true);
        _window?.ShowAt(decision.Companion, state.Handle);
    }

    private void ShowFullscreenOverlay(CodexWindowState state, PixelRect workArea)
    {
        CompanionLayoutDecision decision = CompanionLayoutPlanner.CreateFullscreenOverlay(
            workArea,
            state.Dpi);
        _toggleWindow?.Hide();
        _window?.SetVisibilityButtonVisible(true);
        _window?.ShowAt(decision.Companion, state.Handle);
    }

    private Task<ManagedIntegrationResult> GetOrStartIntegrationAttempt(
        nint handle,
        PixelRect target,
        CancellationToken cancellationToken)
    {
        if (_integrationAttemptTask is not null
            && _integrationAttemptHandle == handle
            && IsWithinOnePixel(_integrationAttemptTarget, target))
        {
            return _integrationAttemptTask;
        }

        CancelIntegrationAttempt();
        _integrationAttemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        _integrationAttemptHandle = handle;
        _integrationAttemptTarget = target;
        _integrationAttemptTask = _placementCoordinator.EnterAsync(
            handle,
            target,
            _integrationAttemptCancellation.Token);
        return _integrationAttemptTask;
    }

    private void CancelIntegrationAttempt()
    {
        CancellationTokenSource? cancellation = _integrationAttemptCancellation;
        Task<ManagedIntegrationResult>? task = _integrationAttemptTask;
        _integrationAttemptCancellation = null;
        _integrationAttemptTask = null;
        _integrationAttemptHandle = 0;
        _integrationAttemptTarget = default;
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        if (task is null || task.IsCompleted)
        {
            cancellation.Dispose();
            return;
        }

        _ = task.ContinueWith(
            _ => cancellation.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void ShowStableVisibility(CodexWindowState state, bool isFullscreen)
    {
        if (_visibility.State == CompanionVisibilityState.DeferredFullscreenExpand)
        {
            HideCompanionWindows();
            return;
        }

        if (_visibility.State is CompanionVisibilityState.Expanding
            or CompanionVisibilityState.Collapsing)
        {
            _ = _visibility.CompleteAnimation();
            _revealAnimator.SnapTo(
                _settings.Visibility == CompanionVisibilityPreference.Expanded ? 1 : 0);
        }

        if (_settings.Visibility == CompanionVisibilityPreference.Collapsed)
        {
            ShowCollapsed(state, allowCapture: ShouldCaptureAnchor(state));
            return;
        }

        if (isFullscreen)
        {
            PixelRect workArea = _windowTracker.GetWorkArea(state.Handle);
            if (_placementCoordinator.TryGetOwned(
                    state.Handle,
                    out CompanionLayoutMode mode,
                    out _))
            {
                ShowIntegrated(state, workArea, mode);
            }
            else
            {
                ShowFullscreenOverlay(state, workArea);
            }

            return;
        }

        _toggleWindow?.Hide();
        _window?.SetVisibilityButtonVisible(true);
        _window?.ShowAt(
            CompanionLayoutPlanner.ForNormalWindow(state).Companion,
            state.Handle);
    }

    private void ShowCollapsed(CodexWindowState state, bool allowCapture)
    {
        Volatile.Write(ref _fastFollowEnabled, 1);
        _window?.Hide();
        bool firstCaptureForWindow = _capturedAnchorHandle != state.Handle;
        ComposerAnchor anchor = _anchorLocator.Locate(
            state,
            allowCapture || firstCaptureForWindow);
        _capturedAnchorHandle = state.Handle;
        _lastToggleBounds = anchor.ButtonBounds;
        _toggleWindow?.ShowAt(anchor.ButtonBounds, state.Handle);
    }

    private void HideCompanionWindows()
    {
        _anchorCalibrationAnimator.Cancel();
        Interlocked.Increment(ref _anchorCalibrationGeneration);
        _window?.Hide();
        _toggleWindow?.Hide();
    }

    private async Task ToggleVisibilityAsync()
    {
        if (_disposed
            || _lastWindowState is not
            {
                IsVisible: true,
                IsMinimized: false,
            } state)
        {
            return;
        }

        PixelRect workArea = _windowTracker.GetWorkArea(state.Handle);
        bool hasReservedSlot = _placementCoordinator.TryGetOwned(
            state.Handle,
            out _,
            out PixelRect ownedTarget)
            && IsWithinOnePixel(state.Bounds, ownedTarget);
        bool isFullscreen = hasReservedSlot
            || state.IsMaximized
            || FillsWorkArea(state.Bounds, workArea);
        CompanionVisibilityDecision decision = _visibility.Toggle(
            isFullscreen,
            hasReservedSlot,
            _settings.FullscreenReveal);
        _settings = _settings with { Visibility = decision.Preference };
        _ = PersistSettingsAsync();
        int generation = Interlocked.Increment(ref _visibilityGeneration);

        if (decision.IsDeferred)
        {
            await AnimateToggleAwayAsync(state, generation);
            return;
        }

        if (decision.RequiresFullscreenIntegration)
        {
            await AnimateDynamicFullscreenRevealAsync(state, generation);
            return;
        }

        await AnimateVisibilityAsync(
            state,
            expand: decision.Preference == CompanionVisibilityPreference.Expanded,
            generation);
    }

    private async Task AnimateVisibilityAsync(
        CodexWindowState state,
        bool expand,
        int generation)
    {
        _window ??= CreateWindow();
        _toggleWindow ??= CreateToggleWindow();
        CompanionPlacement tray = GetTrayPlacement(state);
        ComposerAnchor anchor = _anchorLocator.Locate(state, allowCapture: true);
        CompanionRevealFrame initial = CompanionRevealFrameCalculator.Calculate(
            tray,
            anchor,
            _revealAnimator.Progress,
            state.Dpi);
        _window.ShowForAnimation(
            tray,
            Math.Max(1, initial.VisibleTrayHeight),
            state.Handle);
        _toggleWindow.ShowAt(
            initial.ToggleBounds,
            state.Handle,
            _window.Handle);
        _lastToggleBounds = initial.ToggleBounds;

        CompanionRevealAnimationResult result = await _revealAnimator.AnimateToAsync(
            expand ? 1 : 0,
            expand ? ExpandDuration : CollapseDuration,
            progress =>
            {
                CodexWindowState? current = _lastWindowState;
                return current is
                {
                    IsVisible: true,
                    IsMinimized: false,
                }
                    && current.Handle == state.Handle
                    && ApplyRevealFrame(progress, current);
            },
            _lifetime.Token);

        if (!result.Completed
            || generation != Volatile.Read(ref _visibilityGeneration)
            || _lastWindowState is not { } finalState
            || finalState.Handle != state.Handle)
        {
            return;
        }

        _ = _visibility.CompleteAnimation();
        if (expand)
        {
            ShowStableVisibility(
                finalState,
                IsFullscreenState(finalState));
        }
        else
        {
            ShowCollapsed(finalState, allowCapture: true);
        }
    }

    private async Task AnimateDynamicFullscreenRevealAsync(
        CodexWindowState state,
        int generation)
    {
        CancelIntegrationAttempt();
        PixelRect workArea = _windowTracker.GetWorkArea(state.Handle);
        CompanionLayoutDecision layout = CompanionLayoutPlanner.CreateIntegrated(
            workArea,
            state.Dpi,
            CompanionLayoutMode.ManagedIntegratedFullscreen);
        PixelRect target = layout.CodexTarget!.Value;
        CompanionPlacement tray = layout.Companion;
        ComposerAnchor anchor = _anchorLocator.Locate(state, allowCapture: true);
        _revealAnimator.SnapTo(0);
        _window ??= CreateWindow();
        _toggleWindow ??= CreateToggleWindow();
        _window.ShowForAnimation(tray, 1, state.Handle);
        _toggleWindow.ShowAt(anchor.ButtonBounds, state.Handle, _window.Handle);
        _lastToggleBounds = anchor.ButtonBounds;

        var guard = new FullscreenRevealGuard();
        Volatile.Write(ref _fastFollowEnabled, 0);
        Volatile.Write(ref _dynamicFullscreenReveal, 1);
        ManagedIntegrationResult integration;
        try
        {
            integration = await _placementCoordinator.EnterManagedAnimatedAsync(
                state.Handle,
                target,
                async (start, end, apply, read, cancellationToken) =>
                {
                    TimeSpan previousFrame = TimeSpan.Zero;
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    CompanionRevealAnimationResult animation = await _revealAnimator.AnimateToAsync(
                        1,
                        FullscreenExpandDuration,
                        progress =>
                        {
                            CodexWindowState? observed = _lastWindowState;
                            if (observed is null
                                || observed.Handle != state.Handle
                                || observed.Dpi != state.Dpi
                                || observed.ChangeKind == WindowChangeKind.MoveSizeStarted
                                || _windowTracker.GetWorkArea(state.Handle) != workArea)
                            {
                                _ = guard.Interrupt();
                                return false;
                            }

                            TimeSpan elapsed = stopwatch.Elapsed;
                            TimeSpan interval = elapsed - previousFrame;
                            previousFrame = elapsed;
                            PixelRect frameTarget = InterpolateRect(start, end, progress);
                            if (!apply(frameTarget))
                            {
                                return false;
                            }

                            PixelRect actual = read();
                            FullscreenRevealFailureReason? failure = guard.ReportFrame(
                                interval,
                                MaximumDeviation(actual, frameTarget));
                            failure ??= guard.CheckElapsed(elapsed);
                            CompanionRevealFrame frame = CompanionRevealFrameCalculator.Calculate(
                                tray,
                                anchor,
                                progress,
                                state.Dpi);
                            _lastToggleBounds = frame.ToggleBounds;
                            bool companionApplied = _window.TryAnimateReveal(
                                tray,
                                frame.VisibleTrayHeight)
                                && _toggleWindow.TryFastReposition(frame.ToggleBounds);
                            return failure is null && companionApplied;
                        },
                        cancellationToken);
                    return animation.Completed
                        && guard.CheckElapsed(stopwatch.Elapsed) is null;
                },
                _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        finally
        {
            Volatile.Write(ref _dynamicFullscreenReveal, 0);
        }

        if (generation != Volatile.Read(ref _visibilityGeneration)
            || _lastWindowState is not { } current
            || current.Handle != state.Handle)
        {
            return;
        }

        if (integration.Mode == CompanionLayoutMode.ManagedIntegratedFullscreen)
        {
            _ = _visibility.CompleteAnimation();
            _revealAnimator.SnapTo(1);
            ShowIntegrated(
                current,
                workArea,
                CompanionLayoutMode.ManagedIntegratedFullscreen);
            return;
        }

        _ = _visibility.FullscreenRevealFailed();
        await AnimateToggleAwayAsync(current, generation);
    }

    private async Task AnimateToggleAwayAsync(
        CodexWindowState state,
        int generation)
    {
        _window?.Hide();
        _toggleWindow ??= CreateToggleWindow();
        PixelRect start = _lastToggleBounds.Width > 0
            ? _lastToggleBounds
            : _anchorLocator.Locate(state, allowCapture: false).ButtonBounds;
        int top = state.Bounds.Bottom + 2;
        PixelRect target = new(
            start.Left,
            top,
            start.Right,
            top + start.Height);
        _toggleWindow.ShowAt(start, state.Handle);
        _revealAnimator.SnapTo(0);
        CompanionRevealAnimationResult result = await _revealAnimator.AnimateToAsync(
            1,
            TimeSpan.FromMilliseconds(120),
            progress =>
            {
                PixelRect frame = InterpolateRect(start, target, progress);
                _lastToggleBounds = frame;
                return _toggleWindow.TryFastReposition(frame);
            },
            _lifetime.Token);
        if (result.Completed
            && generation == Volatile.Read(ref _visibilityGeneration))
        {
            _toggleWindow.Hide();
        }
    }

    private bool ApplyRevealFrame(double progress, CodexWindowState state)
    {
        if (_window is null || _toggleWindow is null)
        {
            return false;
        }

        CompanionPlacement tray = GetTrayPlacement(state);
        ComposerAnchor anchor = _anchorLocator.Locate(state, allowCapture: false);
        CompanionRevealFrame frame = CompanionRevealFrameCalculator.Calculate(
            tray,
            anchor,
            progress,
            state.Dpi);
        _lastToggleBounds = frame.ToggleBounds;
        return _window.TryAnimateReveal(tray, frame.VisibleTrayHeight)
            && _toggleWindow.TryFastReposition(frame.ToggleBounds);
    }

    private CompanionPlacement GetTrayPlacement(CodexWindowState state)
    {
        PixelRect workArea = _windowTracker.GetWorkArea(state.Handle);
        if (_placementCoordinator.TryGetOwned(
                state.Handle,
                out CompanionLayoutMode mode,
                out _))
        {
            return CompanionLayoutPlanner.CreateIntegrated(
                workArea,
                state.Dpi,
                mode).Companion;
        }

        return IsFullscreenState(state)
            ? CompanionLayoutPlanner.CreateFullscreenOverlay(workArea, state.Dpi).Companion
            : CompanionLayoutPlanner.ForNormalWindow(state).Companion;
    }

    private bool IsFullscreenState(CodexWindowState state)
    {
        PixelRect workArea = _windowTracker.GetWorkArea(state.Handle);
        return state.IsMaximized || FillsWorkArea(state.Bounds, workArea);
    }

    private void OnAnchorUpdated(ComposerAnchorUpdate update)
    {
        if (_disposed)
        {
            return;
        }

        _ = Application.Current.Dispatcher.InvokeAsync(
            () => ApplyAnchorUpdate(update),
            DispatcherPriority.Background);
    }

    private void ApplyAnchorUpdate(ComposerAnchorUpdate update)
    {
        if (_disposed
            || _visibility.State != CompanionVisibilityState.Collapsed
            || _lastWindowState is not
            {
                IsVisible: true,
                IsMinimized: false,
            } state
            || state.Handle != update.Handle
            || state.Dpi != update.Dpi
            || state.Bounds.Width != update.WindowBounds.Width
            || state.Bounds.Height != update.WindowBounds.Height
            || _toggleWindow is null)
        {
            return;
        }

        PixelRect current = _lastToggleBounds.Width > 0
            ? _lastToggleBounds
            : update.Anchor.ButtonBounds;
        int horizontalDelta = state.Bounds.Left - update.WindowBounds.Left;
        int verticalDelta = state.Bounds.Top - update.WindowBounds.Top;
        PixelRect target = new(
            update.Anchor.ButtonBounds.Left + horizontalDelta,
            update.Anchor.ButtonBounds.Top + verticalDelta,
            update.Anchor.ButtonBounds.Right + horizontalDelta,
            update.Anchor.ButtonBounds.Bottom + verticalDelta);
        AnchorCalibrationPlan plan = AnchorCalibrationPlanner.Create(
            current,
            target,
            Volatile.Read(ref _moveSizeActive) != 0);
        if (plan.Action == AnchorCalibrationAction.None)
        {
            _lastToggleBounds = target;
            return;
        }

        if (plan.Action == AnchorCalibrationAction.Defer)
        {
            return;
        }

        int generation = Interlocked.Increment(ref _anchorCalibrationGeneration);
        _anchorCalibrationAnimator.SnapTo(0);
        _ = AnimateAnchorCalibrationAsync(
            state.Handle,
            current,
            target,
            plan.Duration,
            generation);
    }

    private async Task AnimateAnchorCalibrationAsync(
        nint handle,
        PixelRect start,
        PixelRect target,
        TimeSpan duration,
        int generation)
    {
        CompanionRevealAnimationResult result = await
            _anchorCalibrationAnimator.AnimateToAsync(
                1,
                duration,
                progress =>
                {
                    CodexWindowState? state = _lastWindowState;
                    if (_disposed
                        || generation != Volatile.Read(
                            ref _anchorCalibrationGeneration)
                        || Volatile.Read(ref _moveSizeActive) != 0
                        || _visibility.State != CompanionVisibilityState.Collapsed
                        || state is null
                        || state.Handle != handle
                        || _toggleWindow is null)
                    {
                        return false;
                    }

                    PixelRect frame = InterpolateRect(start, target, progress);
                    _lastToggleBounds = frame;
                    return _toggleWindow.TryFastReposition(frame);
                },
                _lifetime.Token);
        if (result.Completed
            && generation == Volatile.Read(ref _anchorCalibrationGeneration))
        {
            _lastToggleBounds = target;
        }
    }

    private CompanionUserSettings LoadSettings()
    {
        try
        {
            return _settingsStore.LoadAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch (IOException)
        {
            return CompanionUserSettings.Default;
        }
        catch (UnauthorizedAccessException)
        {
            return CompanionUserSettings.Default;
        }
    }

    private async Task PersistSettingsAsync()
    {
        try
        {
            await _settingsStore.SaveAsync(_settings, _lifetime.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool ShouldCaptureAnchor(CodexWindowState state) =>
        state.ChangeKind is WindowChangeKind.Foreground
            or WindowChangeKind.MoveSizeEnded;

    private static PixelRect InterpolateRect(
        PixelRect start,
        PixelRect end,
        double progress) =>
        new(
            CompanionAnimationMath.Interpolate(start.Left, end.Left, progress),
            CompanionAnimationMath.Interpolate(start.Top, end.Top, progress),
            CompanionAnimationMath.Interpolate(start.Right, end.Right, progress),
            CompanionAnimationMath.Interpolate(start.Bottom, end.Bottom, progress));

    private static int MaximumDeviation(PixelRect actual, PixelRect target) =>
        Math.Max(
            Math.Max(
                Math.Abs(actual.Left - target.Left),
                Math.Abs(actual.Top - target.Top)),
            Math.Max(
                Math.Abs(actual.Right - target.Right),
                Math.Abs(actual.Bottom - target.Bottom)));

    private UsageWindow CreateWindow()
    {
        var window = new UsageWindow();
        window.VisibilityToggleRequested += OnVisibilityToggleRequested;
        window.RefreshRequested += OnRefreshRequested;
        window.ApplySnapshot(_lastSnapshot);
        return window;
    }

    private CollapsedToggleWindow CreateToggleWindow()
    {
        var window = new CollapsedToggleWindow();
        window.VisibilityToggleRequested += OnVisibilityToggleRequested;
        return window;
    }

    private async Task EnsureUsageForVisibleWindowAsync(bool refreshExisting)
    {
        try
        {
            await _dataLock.WaitAsync(_lifetime.Token).ConfigureAwait(false);
            try
            {
                if (_usageClient is null)
                {
                    await CreateAndStartUsageClientCoreAsync().ConfigureAwait(false);
                }
                else if (refreshExisting)
                {
                    await _usageClient.RefreshAsync(_lifetime.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                _dataLock.Release();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async Task CreateAndStartUsageClientCoreAsync()
    {
        string? executablePath = FindCodexCli();
        if (executablePath is null)
        {
            PublishLocalSnapshot(new UsageSnapshot(
                null,
                null,
                null,
                null,
                SyncState.Offline,
                DateTimeOffset.Now));
            return;
        }

        var session = new StdioCodexAppServerSession(executablePath);
        var client = new CodexUsageClient(session, new SystemClock(), TimeSpan.FromMinutes(1));
        client.SnapshotChanged += OnSnapshotChanged;
        _usageClient = client;
        await client.StartAsync(_lifetime.Token).ConfigureAwait(false);
    }

    private async Task DisposeUsageClientSafelyAsync()
    {
        try
        {
            await _dataLock.WaitAsync(_lifetime.Token).ConfigureAwait(false);
            try
            {
                await DisposeUsageClientCoreAsync().ConfigureAwait(false);
            }
            finally
            {
                _dataLock.Release();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async Task DisposeUsageClientCoreAsync()
    {
        ICodexUsageClient? client = _usageClient;
        _usageClient = null;
        if (client is null)
        {
            return;
        }

        client.SnapshotChanged -= OnSnapshotChanged;
        await client.DisposeAsync().ConfigureAwait(false);
    }

    private void OnSnapshotChanged(object? sender, UsageSnapshot snapshot)
    {
        if (_disposed)
        {
            return;
        }

        PublishLocalSnapshot(snapshot);
        _authenticationRetryTimer.Change(
            snapshot.State == SyncState.AuthenticationRequired
                ? AuthenticationRetryInterval
                : Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    private void PublishLocalSnapshot(UsageSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        _ = Application.Current.Dispatcher.InvokeAsync(
            () => _window?.ApplySnapshot(snapshot),
            DispatcherPriority.DataBind);
    }

    private void ScheduleAuthenticationRetry()
    {
        if (_disposed)
        {
            return;
        }

        _ = Application.Current.Dispatcher.InvokeAsync(
            () => _ = RebuildUsageSessionAsync(),
            DispatcherPriority.Background);
    }

    private void OnVisibilityToggleRequested() =>
        _ = ToggleVisibilityAsync();

    private void OnRefreshRequested() =>
        _ = RebuildUsageSessionAsync();

    private async Task RebuildUsageSessionAsync()
    {
        try
        {
            await _dataLock.WaitAsync(_lifetime.Token).ConfigureAwait(false);
            try
            {
                PublishLocalSnapshot((_usageClient?.CurrentSnapshot ?? _lastSnapshot) with
                {
                    State = SyncState.Syncing,
                    UpdatedAt = DateTimeOffset.Now,
                });
                await DisposeUsageClientCoreAsync().ConfigureAwait(false);
                if (_lastWindowState is { IsVisible: true, IsMinimized: false })
                {
                    await CreateAndStartUsageClientCoreAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                _dataLock.Release();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private static bool FillsWorkArea(PixelRect bounds, PixelRect workArea) =>
        Math.Abs(bounds.Left - workArea.Left) <= 1
        && Math.Abs(bounds.Top - workArea.Top) <= 1
        && Math.Abs(bounds.Right - workArea.Right) <= 1
        && Math.Abs(bounds.Bottom - workArea.Bottom) <= 1;

    private static bool IsWithinOnePixel(PixelRect left, PixelRect right) =>
        Math.Abs(left.Left - right.Left) <= 1
        && Math.Abs(left.Top - right.Top) <= 1
        && Math.Abs(left.Right - right.Right) <= 1
        && Math.Abs(left.Bottom - right.Bottom) <= 1;

    private static string? FindCodexCli() => CodexExecutableLocator.Find();
}
