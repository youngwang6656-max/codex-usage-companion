namespace CodexUsageCompanion.Windows;

public enum NormalRestorePointSource
{
    ObservedNormal,
    WindowPlacement,
}

public sealed record NormalWindowRestorePoint(
    nint Handle,
    PixelRect Bounds,
    uint Dpi,
    NormalRestorePointSource Source);

public sealed record ManagedWindowSnapshot(
    PixelRect ExtendedBounds,
    bool WasMaximized,
    byte[] NativePlacement,
    NormalWindowRestorePoint? WindowPlacementRestorePoint = null);

public readonly record struct ManagedIntegrationResult(
    CompanionLayoutMode Mode,
    PixelRect ActualBounds);

public readonly record struct ManagedRestoreResult(
    bool Succeeded,
    PixelRect ActualBounds,
    int Attempts);

public interface IManagedWindowNative
{
    ManagedWindowSnapshot Capture(nint handle);

    PixelRect GetExtendedBounds(nint handle);

    bool IsMaximized(nint handle);

    bool TryApplyNative(nint handle, PixelRect target);

    bool TryApplyManaged(nint handle, PixelRect target);

    bool TryRestore(nint handle, ManagedWindowSnapshot snapshot);

    NormalWindowRestorePoint ResolveRestorePoint(
        nint handle,
        NormalWindowRestorePoint restorePoint);

    bool TryRestoreNormal(
        nint handle,
        ManagedWindowSnapshot snapshot,
        NormalWindowRestorePoint restorePoint);
}

public interface IManagedWindowDelay
{
    Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public delegate Task<bool> ManagedWindowAnimation(
    PixelRect start,
    PixelRect target,
    Func<PixelRect, bool> apply,
    Func<PixelRect> read,
    CancellationToken cancellationToken);

public sealed class SystemManagedWindowDelay : IManagedWindowDelay
{
    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

public sealed class ManagedWindowPlacementCoordinator
{
    private static readonly TimeSpan NativeStabilityDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ManagedStabilityDelay = TimeSpan.FromMilliseconds(32);
    private static readonly TimeSpan RestoreVerificationDelay = TimeSpan.FromMilliseconds(50);
    private const int RestoreAttempts = 3;
    private readonly IManagedWindowNative _native;
    private readonly IManagedWindowDelay _delay;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _transitionLock = new(1, 1);
    private readonly HashSet<nint> _failedWindows = [];
    private readonly HashSet<nint> _transitionWindows = [];
    private readonly Dictionary<nint, NormalWindowRestorePoint> _normalRestorePoints = [];
    private readonly Dictionary<nint, OwnedWindow> _ownedWindows = [];

    public ManagedWindowPlacementCoordinator(
        IManagedWindowNative native,
        IManagedWindowDelay? delay = null)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _delay = delay ?? new SystemManagedWindowDelay();
    }

    public void RecordNormalBounds(nint handle, PixelRect bounds, uint dpi)
    {
        if (handle == 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        lock (_gate)
        {
            if (_ownedWindows.ContainsKey(handle) || _transitionWindows.Contains(handle))
            {
                return;
            }

            _normalRestorePoints[handle] = new NormalWindowRestorePoint(
                handle,
                bounds,
                dpi == 0 ? 96u : dpi,
                NormalRestorePointSource.ObservedNormal);
        }
    }

    public async Task<ManagedIntegrationResult> EnterAsync(
        nint handle,
        PixelRect target,
        CancellationToken cancellationToken)
    {
        await _transitionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                _transitionWindows.Add(handle);
                if (_failedWindows.Contains(handle))
                {
                    return new ManagedIntegrationResult(
                        CompanionLayoutMode.FullscreenOverlay,
                        _native.GetExtendedBounds(handle));
                }

                if (_ownedWindows.TryGetValue(handle, out OwnedWindow? owned)
                    && IsWithinTolerance(owned.Target, target, tolerance: 1)
                    && IsWithinTolerance(_native.GetExtendedBounds(handle), target, tolerance: 1))
                {
                    return new ManagedIntegrationResult(owned.Mode, target);
                }
            }

            ManagedWindowSnapshot original = _native.Capture(handle);
            NormalWindowRestorePoint restorePoint = GetRestorePoint(handle, original);
            try
            {
                if (_native.TryApplyNative(handle, target))
                {
                    await _delay.WaitAsync(NativeStabilityDelay, cancellationToken).ConfigureAwait(false);
                    PixelRect nativeBounds = _native.GetExtendedBounds(handle);
                    if (_native.IsMaximized(handle)
                        && IsWithinTolerance(nativeBounds, target, tolerance: 1))
                    {
                        SetOwned(
                            handle,
                            original,
                            restorePoint,
                            target,
                            CompanionLayoutMode.NativeIntegratedFullscreen);
                        return new ManagedIntegrationResult(
                            CompanionLayoutMode.NativeIntegratedFullscreen,
                            nativeBounds);
                    }
                }

                _native.TryRestore(handle, original);
                if (_native.TryApplyManaged(handle, target))
                {
                    await _delay.WaitAsync(ManagedStabilityDelay, cancellationToken).ConfigureAwait(false);
                    PixelRect managedBounds = _native.GetExtendedBounds(handle);
                    if (IsWithinTolerance(managedBounds, target, tolerance: 1))
                    {
                        SetOwned(
                            handle,
                            original,
                            restorePoint,
                            target,
                            CompanionLayoutMode.ManagedIntegratedFullscreen);
                        return new ManagedIntegrationResult(
                            CompanionLayoutMode.ManagedIntegratedFullscreen,
                            managedBounds);
                    }
                }

                _native.TryRestore(handle, original);
                lock (_gate)
                {
                    _ownedWindows.Remove(handle);
                    _failedWindows.Add(handle);
                }

                return new ManagedIntegrationResult(
                    CompanionLayoutMode.FullscreenOverlay,
                    _native.GetExtendedBounds(handle));
            }
            catch (OperationCanceledException)
            {
                _native.TryRestore(handle, original);
                lock (_gate)
                {
                    _ownedWindows.Remove(handle);
                }

                throw;
            }
        }
        finally
        {
            lock (_gate)
            {
                _transitionWindows.Remove(handle);
            }

            _transitionLock.Release();
        }
    }

    public async Task<ManagedIntegrationResult> EnterManagedAnimatedAsync(
        nint handle,
        PixelRect target,
        ManagedWindowAnimation animation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(animation);
        await _transitionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                _transitionWindows.Add(handle);
                if (_failedWindows.Contains(handle))
                {
                    return new ManagedIntegrationResult(
                        CompanionLayoutMode.FullscreenOverlay,
                        _native.GetExtendedBounds(handle));
                }

                if (_ownedWindows.TryGetValue(handle, out OwnedWindow? owned)
                    && IsWithinTolerance(owned.Target, target, tolerance: 1)
                    && IsWithinTolerance(_native.GetExtendedBounds(handle), target, tolerance: 1))
                {
                    return new ManagedIntegrationResult(owned.Mode, target);
                }
            }

            ManagedWindowSnapshot original = _native.Capture(handle);
            NormalWindowRestorePoint restorePoint = GetRestorePoint(handle, original);
            try
            {
                bool completed = await animation(
                    original.ExtendedBounds,
                    target,
                    frame => _native.TryApplyManaged(handle, frame),
                    () => _native.GetExtendedBounds(handle),
                    cancellationToken).ConfigureAwait(false);
                PixelRect actual = _native.GetExtendedBounds(handle);
                if (completed && IsWithinTolerance(actual, target, tolerance: 1))
                {
                    SetOwned(
                        handle,
                        original,
                        restorePoint,
                        target,
                        CompanionLayoutMode.ManagedIntegratedFullscreen);
                    return new ManagedIntegrationResult(
                        CompanionLayoutMode.ManagedIntegratedFullscreen,
                        actual);
                }

                _native.TryRestore(handle, original);
                lock (_gate)
                {
                    _ownedWindows.Remove(handle);
                    _failedWindows.Add(handle);
                }

                return new ManagedIntegrationResult(
                    CompanionLayoutMode.FullscreenOverlay,
                    _native.GetExtendedBounds(handle));
            }
            catch (OperationCanceledException)
            {
                _native.TryRestore(handle, original);
                lock (_gate)
                {
                    _ownedWindows.Remove(handle);
                }

                throw;
            }
        }
        finally
        {
            lock (_gate)
            {
                _transitionWindows.Remove(handle);
            }

            _transitionLock.Release();
        }
    }

    public void ClearIntegrationFailure(nint handle)
    {
        lock (_gate)
        {
            _failedWindows.Remove(handle);
        }
    }

    public async Task<bool> RestoreIfOwnedAsync(
        nint handle,
        CancellationToken cancellationToken)
    {
        await _transitionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return RestoreOwnedCore(handle);
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    public async Task<int> RestoreAllOwnedAsync(CancellationToken cancellationToken)
    {
        await _transitionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            nint[] handles;
            lock (_gate)
            {
                handles = [.. _ownedWindows.Keys];
            }

            int restored = 0;
            foreach (nint handle in handles)
            {
                if (RestoreOwnedCore(handle))
                {
                    restored++;
                }
            }

            return restored;
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    public async Task<ManagedRestoreResult> RestoreForUserActionAsync(
        nint handle,
        CancellationToken cancellationToken)
    {
        await _transitionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            OwnedWindow? owned;
            lock (_gate)
            {
                if (!_ownedWindows.TryGetValue(handle, out owned))
                {
                    return new ManagedRestoreResult(
                        false,
                        _native.GetExtendedBounds(handle),
                        0);
                }

                _transitionWindows.Add(handle);
            }

            NormalWindowRestorePoint restorePoint = _native.ResolveRestorePoint(
                handle,
                owned.RestorePoint);
            PixelRect actual = _native.GetExtendedBounds(handle);
            for (int attempt = 1; attempt <= RestoreAttempts; attempt++)
            {
                _native.TryRestoreNormal(handle, owned.Original, restorePoint);
                await _delay.WaitAsync(RestoreVerificationDelay, cancellationToken).ConfigureAwait(false);
                actual = _native.GetExtendedBounds(handle);
                if (!_native.IsMaximized(handle)
                    && IsWithinTolerance(actual, restorePoint.Bounds, tolerance: 1))
                {
                    lock (_gate)
                    {
                        _ownedWindows.Remove(handle);
                    }

                    return new ManagedRestoreResult(true, actual, attempt);
                }
            }

            lock (_gate)
            {
                _ownedWindows.Remove(handle);
                _failedWindows.Add(handle);
            }

            return new ManagedRestoreResult(false, actual, RestoreAttempts);
        }
        finally
        {
            lock (_gate)
            {
                _transitionWindows.Remove(handle);
            }

            _transitionLock.Release();
        }
    }

    public bool TryGetOwned(
        nint handle,
        out CompanionLayoutMode mode,
        out PixelRect target)
    {
        lock (_gate)
        {
            if (_ownedWindows.TryGetValue(handle, out OwnedWindow? owned))
            {
                mode = owned.Mode;
                target = owned.Target;
                return true;
            }
        }

        mode = CompanionLayoutMode.FullscreenOverlay;
        target = default;
        return false;
    }

    public void Forget(nint handle)
    {
        lock (_gate)
        {
            _ownedWindows.Remove(handle);
            _failedWindows.Remove(handle);
            _transitionWindows.Remove(handle);
            _normalRestorePoints.Remove(handle);
        }
    }

    private NormalWindowRestorePoint GetRestorePoint(
        nint handle,
        ManagedWindowSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_normalRestorePoints.TryGetValue(handle, out NormalWindowRestorePoint? observed))
            {
                return observed;
            }
        }

        return snapshot.WindowPlacementRestorePoint
            ?? new NormalWindowRestorePoint(
                handle,
                snapshot.ExtendedBounds,
                96,
                NormalRestorePointSource.WindowPlacement);
    }

    private bool RestoreOwnedCore(nint handle)
    {
        OwnedWindow? owned;
        lock (_gate)
        {
            if (!_ownedWindows.TryGetValue(handle, out owned))
            {
                return false;
            }
        }

        PixelRect current = _native.GetExtendedBounds(handle);
        if (!IsWithinTolerance(current, owned.Target, tolerance: 1))
        {
            lock (_gate)
            {
                _ownedWindows.Remove(handle);
            }

            return false;
        }

        bool restored = _native.TryRestore(handle, owned.Original);
        lock (_gate)
        {
            _ownedWindows.Remove(handle);
        }

        return restored;
    }

    private void SetOwned(
        nint handle,
        ManagedWindowSnapshot original,
        NormalWindowRestorePoint restorePoint,
        PixelRect target,
        CompanionLayoutMode mode)
    {
        lock (_gate)
        {
            _ownedWindows[handle] = new OwnedWindow(original, restorePoint, target, mode);
        }
    }

    private static bool IsWithinTolerance(PixelRect left, PixelRect right, int tolerance) =>
        Math.Abs(left.Left - right.Left) <= tolerance
        && Math.Abs(left.Top - right.Top) <= tolerance
        && Math.Abs(left.Right - right.Right) <= tolerance
        && Math.Abs(left.Bottom - right.Bottom) <= tolerance;

    private sealed record OwnedWindow(
        ManagedWindowSnapshot Original,
        NormalWindowRestorePoint RestorePoint,
        PixelRect Target,
        CompanionLayoutMode Mode);
}
