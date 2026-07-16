using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using CodexUsageCompanion.Windows;

namespace CodexUsageCompanion;

public partial class CollapsedToggleWindow : Window
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

    public CollapsedToggleWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    public event Action? VisibilityToggleRequested;

    public nint Handle => Interlocked.CompareExchange(ref _windowHandle, 0, 0);

    public void ShowAt(
        PixelRect bounds,
        nint codexWindow,
        nint aboveWindow = 0)
    {
        if (!IsVisible)
        {
            Show();
        }

        EnsureHandle();
        _ = PositionNative(bounds, codexWindow, aboveWindow, showWindow: true);
        _ = ShowWindow(_windowHandle, SwShownoactivate);
    }

    public bool TryFastReposition(PixelRect bounds)
    {
        nint handle = Interlocked.CompareExchange(ref _windowHandle, 0, 0);
        return handle != 0
            && SetWindowPos(
                handle,
                0,
                bounds.Left,
                bounds.Top,
                Math.Max(1, bounds.Width),
                Math.Max(1, bounds.Height),
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

    private void OnToggleClicked(object sender, RoutedEventArgs eventArgs) =>
        VisibilityToggleRequested?.Invoke();

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
        PixelRect bounds,
        nint codexWindow,
        nint aboveWindow,
        bool showWindow)
    {
        nint reference = aboveWindow == 0 ? codexWindow : aboveWindow;
        nint previousWindow = GetWindow(reference, GwHwndPrev);
        uint flags = SwpNoactivate | (showWindow ? SwpShowwindow : 0);
        if (previousWindow == 0)
        {
            flags |= SwpNozorder;
        }

        return SetWindowPos(
            _windowHandle,
            previousWindow,
            bounds.Left,
            bounds.Top,
            Math.Max(1, bounds.Width),
            Math.Max(1, bounds.Height),
            flags);
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
}
