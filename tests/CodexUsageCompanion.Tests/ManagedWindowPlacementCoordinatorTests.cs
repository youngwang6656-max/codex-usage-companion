using CodexUsageCompanion.Windows;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class ManagedWindowPlacementCoordinatorTests
{
    private static readonly PixelRect NormalBounds = new(240, 160, 1440, 900);
    private static readonly PixelRect MaximizedBounds = new(0, 0, 1920, 1080);
    private static readonly PixelRect IntegratedTarget = new(0, 0, 1920, 1031);
    private static readonly nint Handle = (nint)7;

    [Fact]
    public async Task EnterAsync_KeepsNativeMaximizedWhenSystemAcceptsTarget()
    {
        var native = new FakeManagedWindowNative { NativeAccepted = true };
        var coordinator = CreateCoordinator(native);
        coordinator.RecordNormalBounds(Handle, NormalBounds, dpi: 96);

        ManagedIntegrationResult result = await coordinator.EnterAsync(
            Handle,
            IntegratedTarget,
            TestContext.Current.CancellationToken);

        Assert.Equal(CompanionLayoutMode.NativeIntegratedFullscreen, result.Mode);
        Assert.Equal(1, native.NativeAttempts);
        Assert.Equal(0, native.ManagedAttempts);
        Assert.Equal(IntegratedTarget, result.ActualBounds);
    }

    [Fact]
    public async Task EnterAsync_UsesManagedFullscreenWhenNativeMaximizedBouncesBack()
    {
        var native = new FakeManagedWindowNative
        {
            NativeAccepted = false,
            ManagedAccepted = true,
        };
        var coordinator = CreateCoordinator(native);
        coordinator.RecordNormalBounds(Handle, NormalBounds, dpi: 96);

        ManagedIntegrationResult result = await coordinator.EnterAsync(
            Handle,
            IntegratedTarget,
            TestContext.Current.CancellationToken);

        Assert.Equal(CompanionLayoutMode.ManagedIntegratedFullscreen, result.Mode);
        Assert.Equal(1, native.NativeAttempts);
        Assert.Equal(1, native.ManagedAttempts);
        Assert.True(native.RestoreCount >= 1);
    }

    [Fact]
    public async Task RestoreForUserActionAsync_RestoresObservedNormalBoundsFromNativeMode()
    {
        var native = new FakeManagedWindowNative { NativeAccepted = true };
        var coordinator = CreateCoordinator(native);
        coordinator.RecordNormalBounds(Handle, NormalBounds, dpi: 144);
        await coordinator.EnterAsync(Handle, IntegratedTarget, TestContext.Current.CancellationToken);

        ManagedRestoreResult result = await coordinator.RestoreForUserActionAsync(
            Handle,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Attempts);
        Assert.Equal(NormalBounds, result.ActualBounds);
        Assert.Equal(NormalBounds, native.LastNormalRestorePoint?.Bounds);
        Assert.Equal(144u, native.LastNormalRestorePoint?.Dpi);
        Assert.Equal(NormalRestorePointSource.ObservedNormal, native.LastNormalRestorePoint?.Source);
        Assert.False(native.Maximized);
    }

    [Fact]
    public async Task RestoreForUserActionAsync_RestoresObservedNormalBoundsFromManagedMode()
    {
        var native = new FakeManagedWindowNative { ManagedAccepted = true };
        var coordinator = CreateCoordinator(native);
        coordinator.RecordNormalBounds(Handle, NormalBounds, dpi: 96);
        await coordinator.EnterAsync(Handle, IntegratedTarget, TestContext.Current.CancellationToken);

        ManagedRestoreResult result = await coordinator.RestoreForUserActionAsync(
            Handle,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(NormalBounds, result.ActualBounds);
        Assert.Equal(1, native.NormalRestoreCount);
    }

    [Fact]
    public async Task RecordNormalBounds_DuringOwnedFullscreenCannotOverwriteFrozenRestorePoint()
    {
        var native = new FakeManagedWindowNative { NativeAccepted = true };
        var coordinator = CreateCoordinator(native);
        coordinator.RecordNormalBounds(Handle, NormalBounds, dpi: 96);
        await coordinator.EnterAsync(Handle, IntegratedTarget, TestContext.Current.CancellationToken);

        coordinator.RecordNormalBounds(Handle, IntegratedTarget, dpi: 96);
        ManagedRestoreResult result = await coordinator.RestoreForUserActionAsync(
            Handle,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(NormalBounds, result.ActualBounds);
    }

    [Fact]
    public async Task EnterAsync_UsesWindowPlacementRestorePointWhenNoNormalBoundsWereObserved()
    {
        var native = new FakeManagedWindowNative
        {
            NativeAccepted = true,
            WindowPlacementBounds = new PixelRect(320, 180, 1520, 920),
        };
        var coordinator = CreateCoordinator(native);
        await coordinator.EnterAsync(Handle, IntegratedTarget, TestContext.Current.CancellationToken);

        ManagedRestoreResult result = await coordinator.RestoreForUserActionAsync(
            Handle,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(native.WindowPlacementBounds, result.ActualBounds);
        Assert.Equal(NormalRestorePointSource.WindowPlacement, native.LastNormalRestorePoint?.Source);
    }

    [Fact]
    public async Task RestoreForUserActionAsync_RetriesUntilBoundsAndZoomStateAreVerified()
    {
        var native = new FakeManagedWindowNative
        {
            NativeAccepted = true,
            NormalRestoreFailuresBeforeSuccess = 2,
        };
        var coordinator = CreateCoordinator(native);
        coordinator.RecordNormalBounds(Handle, NormalBounds, dpi: 96);
        await coordinator.EnterAsync(Handle, IntegratedTarget, TestContext.Current.CancellationToken);

        ManagedRestoreResult result = await coordinator.RestoreForUserActionAsync(
            Handle,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Attempts);
        Assert.Equal(3, native.NormalRestoreCount);
        Assert.False(coordinator.TryGetOwned(Handle, out _, out _));
    }

    [Fact]
    public async Task RestoreForUserActionAsync_AfterThreeFailuresStopsFullscreenReentry()
    {
        var native = new FakeManagedWindowNative
        {
            NativeAccepted = true,
            NormalRestoreFailuresBeforeSuccess = int.MaxValue,
        };
        var coordinator = CreateCoordinator(native);
        coordinator.RecordNormalBounds(Handle, NormalBounds, dpi: 96);
        await coordinator.EnterAsync(Handle, IntegratedTarget, TestContext.Current.CancellationToken);

        ManagedRestoreResult restore = await coordinator.RestoreForUserActionAsync(
            Handle,
            TestContext.Current.CancellationToken);
        int nativeAttemptsBeforeRetry = native.NativeAttempts;
        ManagedIntegrationResult retry = await coordinator.EnterAsync(
            Handle,
            IntegratedTarget,
            TestContext.Current.CancellationToken);

        Assert.False(restore.Succeeded);
        Assert.Equal(3, restore.Attempts);
        Assert.Equal(CompanionLayoutMode.FullscreenOverlay, retry.Mode);
        Assert.Equal(nativeAttemptsBeforeRetry, native.NativeAttempts);
        Assert.False(coordinator.TryGetOwned(Handle, out _, out _));
    }

    [Fact]
    public async Task RestoreIfOwned_OnComponentExitRestoresOriginalMaximizedState()
    {
        var native = new FakeManagedWindowNative { ManagedAccepted = true };
        var coordinator = CreateCoordinator(native);
        coordinator.RecordNormalBounds(Handle, NormalBounds, dpi: 96);
        await coordinator.EnterAsync(Handle, IntegratedTarget, TestContext.Current.CancellationToken);

        bool restored = await coordinator.RestoreIfOwnedAsync(
            Handle,
            TestContext.Current.CancellationToken);

        Assert.True(restored);
        Assert.Equal(MaximizedBounds, native.Bounds);
        Assert.True(native.Maximized);
    }

    [Fact]
    public async Task RestoreAllOwnedAsync_OnExitRestoresEveryOwnedWindow()
    {
        nint secondHandle = (nint)8;
        var native = new FakeManagedWindowNative { ManagedAccepted = true };
        var coordinator = CreateCoordinator(native);
        coordinator.RecordNormalBounds(Handle, NormalBounds, dpi: 96);
        await coordinator.EnterAsync(Handle, IntegratedTarget, TestContext.Current.CancellationToken);
        coordinator.RecordNormalBounds(secondHandle, NormalBounds, dpi: 96);
        await coordinator.EnterAsync(
            secondHandle,
            IntegratedTarget,
            TestContext.Current.CancellationToken);
        int restoresBefore = native.RestoreCount;

        int restored = await coordinator.RestoreAllOwnedAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(2, restored);
        Assert.Equal(restoresBefore + 2, native.RestoreCount);
        Assert.False(coordinator.TryGetOwned(Handle, out _, out _));
        Assert.False(coordinator.TryGetOwned(secondHandle, out _, out _));
    }

    [Fact]
    public async Task RestoreIfOwned_DoesNotOverwriteAUsersLaterWindowMove()
    {
        var native = new FakeManagedWindowNative { ManagedAccepted = true };
        var coordinator = CreateCoordinator(native);
        coordinator.RecordNormalBounds(Handle, NormalBounds, dpi: 96);
        await coordinator.EnterAsync(Handle, IntegratedTarget, TestContext.Current.CancellationToken);
        native.Bounds = new PixelRect(100, 100, 1000, 700);
        int restoresBefore = native.RestoreCount;

        bool restored = await coordinator.RestoreIfOwnedAsync(
            Handle,
            TestContext.Current.CancellationToken);

        Assert.False(restored);
        Assert.Equal(restoresBefore, native.RestoreCount);
    }

    [Fact]
    public async Task EnterAsync_RestoresAndStopsRetryingWhenBothStrategiesFail()
    {
        var native = new FakeManagedWindowNative();
        var coordinator = CreateCoordinator(native);
        coordinator.RecordNormalBounds(Handle, NormalBounds, dpi: 96);

        ManagedIntegrationResult first = await coordinator.EnterAsync(
            Handle,
            IntegratedTarget,
            TestContext.Current.CancellationToken);
        ManagedIntegrationResult second = await coordinator.EnterAsync(
            Handle,
            IntegratedTarget,
            TestContext.Current.CancellationToken);

        Assert.Equal(CompanionLayoutMode.FullscreenOverlay, first.Mode);
        Assert.Equal(CompanionLayoutMode.FullscreenOverlay, second.Mode);
        Assert.Equal(1, native.NativeAttempts);
        Assert.Equal(1, native.ManagedAttempts);
        Assert.True(native.RestoreCount >= 2);
    }

    [Fact]
    public async Task EnterAsync_CancellationRestoresWindowChangedDuringNativeAttempt()
    {
        var native = new FakeManagedWindowNative { NativeAccepted = true };
        var coordinator = new ManagedWindowPlacementCoordinator(native, new CancellingDelay());
        coordinator.RecordNormalBounds(Handle, NormalBounds, dpi: 96);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await coordinator.EnterAsync(
                Handle,
                IntegratedTarget,
                TestContext.Current.CancellationToken));

        Assert.Equal(MaximizedBounds, native.Bounds);
        Assert.Equal(1, native.RestoreCount);
    }

    [Fact]
    public async Task EnterManagedAnimatedAsync_CommitsOwnedTargetAndKeepsNormalRestorePoint()
    {
        var native = new FakeManagedWindowNative { ManagedAccepted = true };
        var coordinator = CreateCoordinator(native);
        coordinator.RecordNormalBounds(Handle, NormalBounds, dpi: 96);

        ManagedIntegrationResult integration = await coordinator.EnterManagedAnimatedAsync(
            Handle,
            IntegratedTarget,
            async (start, target, apply, read, cancellationToken) =>
            {
                Assert.Equal(MaximizedBounds, start);
                Assert.True(apply(new PixelRect(0, 0, 1920, 1055)));
                Assert.True(apply(target));
                Assert.Equal(target, read());
                await Task.CompletedTask;
                return true;
            },
            TestContext.Current.CancellationToken);
        ManagedRestoreResult restore = await coordinator.RestoreForUserActionAsync(
            Handle,
            TestContext.Current.CancellationToken);

        Assert.Equal(CompanionLayoutMode.ManagedIntegratedFullscreen, integration.Mode);
        Assert.Equal(IntegratedTarget, integration.ActualBounds);
        Assert.True(restore.Succeeded);
        Assert.Equal(NormalBounds, restore.ActualBounds);
    }

    [Fact]
    public async Task EnterManagedAnimatedAsync_FailedAnimationRestoresOriginalFullscreen()
    {
        var native = new FakeManagedWindowNative { ManagedAccepted = true };
        var coordinator = CreateCoordinator(native);
        coordinator.RecordNormalBounds(Handle, NormalBounds, dpi: 96);

        ManagedIntegrationResult result = await coordinator.EnterManagedAnimatedAsync(
            Handle,
            IntegratedTarget,
            async (_, _, apply, _, _) =>
            {
                Assert.True(apply(new PixelRect(0, 0, 1920, 1055)));
                await Task.CompletedTask;
                return false;
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(CompanionLayoutMode.FullscreenOverlay, result.Mode);
        Assert.Equal(MaximizedBounds, native.Bounds);
        Assert.False(coordinator.TryGetOwned(Handle, out _, out _));
    }

    private static ManagedWindowPlacementCoordinator CreateCoordinator(
        FakeManagedWindowNative native) =>
        new(native, new ImmediateDelay());

    private sealed class ImmediateDelay : IManagedWindowDelay
    {
        public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CancellingDelay : IManagedWindowDelay
    {
        public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.FromCanceled(new CancellationToken(canceled: true));
    }

    private sealed class FakeManagedWindowNative : IManagedWindowNative
    {
        private readonly Dictionary<nint, FakeWindowState> _windows = [];

        public bool NativeAccepted { get; init; }

        public bool ManagedAccepted { get; init; }

        public PixelRect Bounds
        {
            get => GetWindow(Handle).Bounds;
            set => GetWindow(Handle).Bounds = value;
        }

        public PixelRect WindowPlacementBounds { get; init; } = NormalBounds;

        public bool Maximized
        {
            get => GetWindow(Handle).Maximized;
            set => GetWindow(Handle).Maximized = value;
        }

        public int NativeAttempts { get; private set; }

        public int ManagedAttempts { get; private set; }

        public int RestoreCount { get; private set; }

        public int NormalRestoreCount { get; private set; }

        public int NormalRestoreFailuresBeforeSuccess { get; init; }

        public NormalWindowRestorePoint? LastNormalRestorePoint { get; private set; }

        public ManagedWindowSnapshot Capture(nint handle) => new(
            GetWindow(handle).Bounds,
            GetWindow(handle).Maximized,
            new byte[] { 1 },
            new NormalWindowRestorePoint(
                handle,
                WindowPlacementBounds,
                96,
                NormalRestorePointSource.WindowPlacement));

        public PixelRect GetExtendedBounds(nint handle) => GetWindow(handle).Bounds;

        public bool IsMaximized(nint handle) => GetWindow(handle).Maximized;

        public bool TryApplyNative(nint handle, PixelRect target)
        {
            NativeAttempts++;
            if (NativeAccepted)
            {
                GetWindow(handle).Bounds = target;
                GetWindow(handle).Maximized = true;
            }

            return true;
        }

        public bool TryApplyManaged(nint handle, PixelRect target)
        {
            ManagedAttempts++;
            if (ManagedAccepted)
            {
                GetWindow(handle).Bounds = target;
                GetWindow(handle).Maximized = false;
            }

            return true;
        }

        public bool TryRestore(nint handle, ManagedWindowSnapshot snapshot)
        {
            RestoreCount++;
            GetWindow(handle).Bounds = snapshot.ExtendedBounds;
            GetWindow(handle).Maximized = snapshot.WasMaximized;
            return true;
        }

        public bool TryRestoreNormal(
            nint handle,
            ManagedWindowSnapshot snapshot,
            NormalWindowRestorePoint restorePoint)
        {
            RestoreCount++;
            NormalRestoreCount++;
            LastNormalRestorePoint = restorePoint;
            if (NormalRestoreCount <= NormalRestoreFailuresBeforeSuccess)
            {
                GetWindow(handle).Bounds = IntegratedTarget;
                GetWindow(handle).Maximized = true;
                return false;
            }

            GetWindow(handle).Bounds = restorePoint.Bounds;
            GetWindow(handle).Maximized = false;
            return true;
        }

        public NormalWindowRestorePoint ResolveRestorePoint(
            nint handle,
            NormalWindowRestorePoint restorePoint) => restorePoint;

        private FakeWindowState GetWindow(nint handle)
        {
            if (!_windows.TryGetValue(handle, out FakeWindowState? window))
            {
                window = new FakeWindowState
                {
                    Bounds = MaximizedBounds,
                    Maximized = true,
                };
                _windows.Add(handle, window);
            }

            return window;
        }

        private sealed class FakeWindowState
        {
            public PixelRect Bounds { get; set; }

            public bool Maximized { get; set; }
        }
    }
}
