using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using CodexUsageCompanion.Usage;
using CodexUsageCompanion.Windows;

namespace CodexUsageCompanion;

public partial class UsageWindow : Window
{
    private const int GwlExstyle = -20;
    private const long WsExToolwindow = 0x00000080L;
    private const long WsExNoactivate = 0x08000000L;
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpNozorder = 0x0004;
    private const uint SwpShowwindow = 0x0040;
    private const int SwShownoactivate = 4;
    private const uint GwHwndPrev = 3;
    private nint _windowHandle;
    private int _regionWidth = -1;
    private int _regionHeight = -1;
    private int _regionVisibleHeight = -1;

    public UsageWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    public event Action? VisibilityToggleRequested;

    public event Action? RefreshRequested;

    public nint Handle => Interlocked.CompareExchange(ref _windowHandle, 0, 0);

    public void ApplySnapshot(UsageSnapshot snapshot)
    {
        TimeZoneInfo chinaTime = TryGetChinaTimeZone();
        UsageDisplayModel display = UsageDisplayModel.FromSnapshot(snapshot, chinaTime);

        PlanText.Text = snapshot.PlanLabel is null ? string.Empty : $"· {snapshot.PlanLabel}";
        StatusText.Text = display.StatusText;
        LimitText.Text = display.LimitLabel;
        RemainingText.Text = display.RemainingText;
        ResetTimeText.Text = display.ResetTimeText;
        ResetCreditsText.Text = display.ResetCreditsText;
        SyncDot.Fill = (Brush)FindResource(display.IsSynced ? "SyncedBrush" : "InactiveBrush");
        ProgressFill.Width = display.IsProgressKnown
            ? Math.Clamp(display.ProgressValue, 0, 100)
            : 0;
    }

    public void SetVisibilityButtonVisible(bool visible) =>
        VisibilityButton.Visibility = visible ? Visibility.Visible : Visibility.Hidden;

    public void ShowAt(CompanionPlacement placement, nint codexWindow)
    {
        if (!IsVisible)
        {
            Show();
        }

        EnsureHandle();
        _ = PositionNative(placement, codexWindow, showWindow: true);
        ApplyRoundedRegion(placement, placement.Height);
        _ = ShowWindow(_windowHandle, SwShownoactivate);
    }

    public void ShowForAnimation(
        CompanionPlacement placement,
        int visibleHeight,
        nint codexWindow)
    {
        if (!IsVisible)
        {
            Show();
        }

        EnsureHandle();
        SetVisibilityButtonVisible(false);
        _ = PositionNative(placement, codexWindow, showWindow: true);
        ApplyRoundedRegion(placement, visibleHeight);
        _ = ShowWindow(_windowHandle, SwShownoactivate);
    }

    public bool TryAnimateReveal(
        CompanionPlacement placement,
        int visibleHeight)
    {
        nint handle = Interlocked.CompareExchange(ref _windowHandle, 0, 0);
        if (handle == 0
            || !SetWindowPos(
                handle,
                0,
                placement.Left,
                placement.Top,
                placement.Width,
                placement.Height,
                SwpNoactivate | SwpNozorder))
        {
            return false;
        }

        ApplyRoundedRegion(placement, visibleHeight);
        return true;
    }

    public bool TryFastReposition(CompanionPlacement placement, nint codexWindow)
    {
        nint handle = Interlocked.CompareExchange(ref _windowHandle, 0, 0);
        return handle != 0
            && SetWindowPos(
                handle,
                0,
                placement.Left,
                placement.Top,
                placement.Width,
                placement.Height,
                SwpNoactivate | SwpNozorder);
    }

    protected override void OnActivated(EventArgs eventArgs)
    {
        base.OnActivated(eventArgs);
        if (_windowHandle != 0)
        {
            _ = ShowWindow(_windowHandle, SwShownoactivate);
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs eventArgs)
    {
        EnsureHandle();
        nint extendedStyle = GetWindowLongPtr(_windowHandle, GwlExstyle);
        _ = SetWindowLongPtr(
            _windowHandle,
            GwlExstyle,
            extendedStyle | (nint)(WsExToolwindow | WsExNoactivate));
    }

    private void EnsureHandle()
    {
        if (_windowHandle == 0)
        {
            _windowHandle = new WindowInteropHelper(this).EnsureHandle();
        }
    }

    private bool PositionNative(
        CompanionPlacement placement,
        nint codexWindow,
        bool showWindow,
        nint handle = 0)
    {
        handle = handle == 0
            ? Interlocked.CompareExchange(ref _windowHandle, 0, 0)
            : handle;
        if (handle == 0)
        {
            return false;
        }

        nint previousWindow = GetWindow(codexWindow, GwHwndPrev);
        bool alreadyImmediatelyAboveCodex = previousWindow == handle;
        uint flags = SwpNoactivate
            | (showWindow ? SwpShowwindow : 0)
            | (alreadyImmediatelyAboveCodex ? SwpNozorder : 0);
        return SetWindowPos(
            handle,
            alreadyImmediatelyAboveCodex ? 0 : previousWindow,
            placement.Left,
            placement.Top,
            placement.Width,
            placement.Height,
            flags);
    }

    private void OnVisibilityClicked(object sender, RoutedEventArgs eventArgs) =>
        VisibilityToggleRequested?.Invoke();

    private void OnRefreshClicked(object sender, RoutedEventArgs eventArgs) =>
        RefreshRequested?.Invoke();

    private static TimeZoneInfo TryGetChinaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
    }

    private void ApplyRoundedRegion(CompanionPlacement placement, int visibleHeight)
    {
        int clippedHeight = Math.Clamp(visibleHeight, 1, placement.Height);
        if (_regionWidth == placement.Width
            && _regionHeight == placement.Height
            && _regionVisibleHeight == clippedHeight)
        {
            return;
        }

        RoundRegionGeometry geometry = WindowRegionGeometry.FromPlacement(placement);
        int regionBottom = Math.Min(geometry.Bottom, clippedHeight + 1);
        nint region = CreateRoundRectRgn(
            0,
            0,
            geometry.Right,
            regionBottom,
            Math.Min(geometry.EllipseWidth, geometry.Right),
            Math.Min(geometry.EllipseHeight, regionBottom));
        if (region == 0)
        {
            return;
        }

        if (SetWindowRgn(_windowHandle, region, redraw: true) == 0)
        {
            _ = DeleteObject(region);
            return;
        }

        _regionWidth = placement.Width;
        _regionHeight = placement.Height;
        _regionVisibleHeight = clippedHeight;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint windowHandle, int index, nint newLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint windowHandle, int command);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint windowHandle, uint command);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(
        int left,
        int top,
        int right,
        int bottom,
        int ellipseWidth,
        int ellipseHeight);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(nint windowHandle, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint graphicsObject);
}
