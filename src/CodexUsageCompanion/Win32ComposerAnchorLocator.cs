using System.Runtime.InteropServices;
using CodexUsageCompanion.Windows;

namespace CodexUsageCompanion;

internal interface IComposerAnchorLocator : IDisposable
{
    event Action<ComposerAnchorUpdate>? AnchorUpdated;

    ComposerAnchor Locate(CodexWindowState window, bool allowCapture);

    void Forget(nint handle);
}

internal sealed class DeterministicComposerAnchorLocator : IComposerAnchorLocator
{
    public event Action<ComposerAnchorUpdate>? AnchorUpdated
    {
        add { }
        remove { }
    }

    public ComposerAnchor Locate(CodexWindowState window, bool allowCapture)
    {
        ArgumentNullException.ThrowIfNull(window);
        return ComposerAnchorPlacement.CreateFallback(window.Bounds, window.Dpi);
    }

    public void Forget(nint handle)
    {
    }

    public void Dispose()
    {
    }
}

internal sealed class Win32ComposerAnchorLocator : IComposerAnchorLocator
{
    private const int Srccopy = 0x00CC0020;
    private readonly ComposerAnchorCache _automationCache = new();
    private readonly ComposerAnchorCache _fallbackCache = new();
    private readonly ComposerToolbarCenterStabilizer _toolbarCenter = new();
    private readonly IModelSelectorBoundsProvider _boundsProvider;
    private readonly object _gate = new();
    private nint _cachedHandle;
    private PixelRect _currentBounds;
    private uint _currentDpi;
    private long _generation;
    private bool _disposed;

    public Win32ComposerAnchorLocator()
        : this(new UiaModelSelectorBoundsProvider())
    {
    }

    internal Win32ComposerAnchorLocator(
        IModelSelectorBoundsProvider boundsProvider)
    {
        _boundsProvider = boundsProvider;
        _boundsProvider.BoundsResolved += OnBoundsResolved;
    }

    public event Action<ComposerAnchorUpdate>? AnchorUpdated;

    public ComposerAnchor Locate(CodexWindowState window, bool allowCapture)
    {
        ArgumentNullException.ThrowIfNull(window);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_cachedHandle != window.Handle)
            {
                _automationCache.Clear();
                _fallbackCache.Clear();
                _toolbarCenter.Clear();
                _cachedHandle = window.Handle;
                _generation++;
            }

            _currentBounds = window.Bounds;
            _currentDpi = window.Dpi;
        }

        if (allowCapture)
        {
            long generation;
            lock (_gate)
            {
                generation = _generation;
            }

            _boundsProvider.Request(window, generation);
        }

        ComposerAnchor? capturedFallback = null;
        if (allowCapture
            && GetForegroundWindow() == window.Handle
            && TryCapture(window, out ComposerCapture capture))
        {
            ComposerAnchor exclusion;
            if (_automationCache.TryResolve(
                    window.Bounds,
                    window.Dpi,
                    out ComposerAnchor currentAutomation))
            {
                exclusion = _toolbarCenter.TryResolve(
                        window.Bounds,
                        window.Dpi,
                        out int currentCenterY)
                    ? RecenterVertically(currentAutomation, currentCenterY)
                    : currentAutomation;
            }
            else
            {
                exclusion = _fallbackCache.TryResolve(
                    window.Bounds,
                    window.Dpi,
                    out ComposerAnchor currentFallback)
                    ? currentFallback
                    : ComposerAnchorPlacement.CreateFallback(
                        window.Bounds,
                        window.Dpi);
            }

            MaskToggle(capture, exclusion.ButtonBounds);
            if (ComposerAnchorDetector.TryDetectLandmarks(
                    capture,
                    out ComposerLandmarks landmarks))
            {
                int stableCenterY = _toolbarCenter.Observe(
                    window.Bounds,
                    window.Dpi,
                    landmarks.SendCenterY);
                ComposerAnchor detected =
                    ComposerAnchorPlacement.CreateFromComposerLandmarks(
                        window.Bounds,
                        landmarks with { SendCenterY = stableCenterY },
                        window.Dpi);
                lock (_gate)
                {
                    if (_cachedHandle == window.Handle)
                    {
                        _fallbackCache.Store(window.Bounds, window.Dpi, detected);
                    }
                }

                capturedFallback = detected;
            }
        }

        if (_automationCache.TryResolve(
                window.Bounds,
                window.Dpi,
                out ComposerAnchor automation))
        {
            return _toolbarCenter.TryResolve(
                    window.Bounds,
                    window.Dpi,
                    out int rowCenterY)
                ? RecenterVertically(automation, rowCenterY)
                : automation;
        }

        if (capturedFallback is { } detectedFallback)
        {
            return detectedFallback;
        }

        if (_fallbackCache.TryResolve(
                window.Bounds,
                window.Dpi,
                out ComposerAnchor cached))
        {
            return cached;
        }

        return ComposerAnchorPlacement.CreateFallback(window.Bounds, window.Dpi);
    }

    public void Forget(nint handle)
    {
        lock (_gate)
        {
            if (_cachedHandle == handle)
            {
                _automationCache.Clear();
                _fallbackCache.Clear();
                _toolbarCenter.Clear();
                _cachedHandle = 0;
                _generation++;
            }
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
            _generation++;
            _automationCache.Clear();
            _fallbackCache.Clear();
            _toolbarCenter.Clear();
        }

        _boundsProvider.BoundsResolved -= OnBoundsResolved;
        _boundsProvider.Dispose();
    }

    private void OnBoundsResolved(ModelSelectorBoundsUpdate update)
    {
        ComposerAnchorUpdate? published = null;
        lock (_gate)
        {
            if (_disposed
                || update.Handle != _cachedHandle
                || update.Generation != _generation
                || update.Dpi != _currentDpi
                || update.WindowBounds.Width != _currentBounds.Width
                || update.WindowBounds.Height != _currentBounds.Height)
            {
                return;
            }

            int rowCenterY = _toolbarCenter.TryResolve(
                update.WindowBounds,
                update.Dpi,
                out int visualCenterY)
                ? visualCenterY
                : update.SelectorBounds.Top + (update.SelectorBounds.Height / 2);
            ComposerAnchor anchor = ComposerAnchorPlacement.CreateForModelSelector(
                update.SelectorBounds,
                update.WindowBounds,
                update.Dpi,
                update.Confidence,
                rowCenterY);
            _automationCache.Store(update.WindowBounds, update.Dpi, anchor);
            if (_automationCache.TryResolve(
                    _currentBounds,
                    _currentDpi,
                    out ComposerAnchor translated))
            {
                published = new ComposerAnchorUpdate(
                    update.Handle,
                    _currentDpi,
                    _currentBounds,
                    translated);
            }
        }

        if (published is { } resolved)
        {
            AnchorUpdated?.Invoke(resolved);
        }
    }

    private static ComposerAnchor RecenterVertically(
        ComposerAnchor anchor,
        int rowCenterY)
    {
        int height = anchor.ButtonBounds.Height;
        int top = rowCenterY - (height / 2);
        return anchor with
        {
            ButtonBounds = new PixelRect(
                anchor.ButtonBounds.Left,
                top,
                anchor.ButtonBounds.Right,
                top + height),
        };
    }

    private static bool TryCapture(
        CodexWindowState window,
        out ComposerCapture capture)
    {
        int requestedWidth = ComposerAnchorPlacement.Scale(260, window.Dpi);
        int requestedHeight = ComposerAnchorPlacement.Scale(220, window.Dpi);
        int width = Math.Min(window.Bounds.Width, requestedWidth);
        int height = Math.Min(window.Bounds.Height, requestedHeight);
        if (width <= 0 || height <= 0)
        {
            capture = default;
            return false;
        }

        int left = window.Bounds.Right - width;
        int top = window.Bounds.Bottom - height;
        nint screenDc = GetDC(0);
        nint memoryDc = 0;
        nint bitmap = 0;
        nint previous = 0;
        try
        {
            if (screenDc == 0)
            {
                capture = default;
                return false;
            }

            memoryDc = CreateCompatibleDC(screenDc);
            if (memoryDc == 0)
            {
                capture = default;
                return false;
            }

            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0,
                },
            };
            bitmap = CreateDIBSection(
                screenDc,
                ref info,
                0,
                out nint bits,
                0,
                0);
            if (bitmap == 0 || bits == 0)
            {
                capture = default;
                return false;
            }

            previous = SelectObject(memoryDc, bitmap);
            if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, left, top, Srccopy))
            {
                capture = default;
                return false;
            }

            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            capture = new ComposerCapture(
                pixels,
                width,
                height,
                stride,
                new PixelRect(left, top, left + width, top + height),
                window.Dpi);
            return true;
        }
        finally
        {
            if (previous != 0 && memoryDc != 0)
            {
                _ = SelectObject(memoryDc, previous);
            }

            if (bitmap != 0)
            {
                _ = DeleteObject(bitmap);
            }

            if (memoryDc != 0)
            {
                _ = DeleteDC(memoryDc);
            }

            if (screenDc != 0)
            {
                _ = ReleaseDC(0, screenDc);
            }
        }
    }

    private static void MaskToggle(ComposerCapture capture, PixelRect screenBounds)
    {
        const byte background = 24;
        int padding = ComposerAnchorPlacement.Scale(4, capture.Dpi);
        int left = Math.Clamp(
            screenBounds.Left - capture.ScreenBounds.Left - padding,
            0,
            capture.Width);
        int top = Math.Clamp(
            screenBounds.Top - capture.ScreenBounds.Top - padding,
            0,
            capture.Height);
        int right = Math.Clamp(
            screenBounds.Right - capture.ScreenBounds.Left + padding,
            0,
            capture.Width);
        int bottom = Math.Clamp(
            screenBounds.Bottom - capture.ScreenBounds.Top + padding,
            0,
            capture.Height);
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                int index = (y * capture.Stride) + (x * 4);
                capture.BgraPixels[index] = background;
                capture.BgraPixels[index + 1] = background;
                capture.BgraPixels[index + 2] = background;
                capture.BgraPixels[index + 3] = 255;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPixelsPerMeter;
        public int YPixelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint windowHandle, nint deviceContext);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint deviceContext);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint deviceContext);

    [DllImport("gdi32.dll")]
    private static extern nint CreateDIBSection(
        nint deviceContext,
        ref BitmapInfo bitmapInfo,
        uint usage,
        out nint bits,
        nint section,
        uint offset);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint deviceContext, nint graphicsObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint graphicsObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(
        nint destination,
        int destinationX,
        int destinationY,
        int width,
        int height,
        nint source,
        int sourceX,
        int sourceY,
        int rasterOperation);
}
