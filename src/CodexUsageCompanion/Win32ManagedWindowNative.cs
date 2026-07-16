using System.Runtime.InteropServices;
using CodexUsageCompanion.Windows;

namespace CodexUsageCompanion;

internal sealed class Win32ManagedWindowNative : IManagedWindowNative
{
    private const int DwmwaExtendedFrameBounds = 9;
    private const int MonitorDefaultToNearest = 2;
    private const int SwRestore = 9;
    private const int SwShownormal = 1;
    private const uint SwpNozorder = 0x0004;
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpFramechanged = 0x0020;
    private const uint SwpNoownerzorder = 0x0200;

    public ManagedWindowSnapshot Capture(nint handle)
    {
        var placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
        if (!GetWindowPlacement(handle, ref placement))
        {
            placement = default;
            placement.Length = Marshal.SizeOf<WindowPlacement>();
        }

        byte[] bytes = new byte[Marshal.SizeOf<WindowPlacement>()];
        nint buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.StructureToPtr(placement, buffer, fDeleteOld: false);
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        PixelRect normalWorkspaceBounds = new(
            placement.NormalPosition.Left,
            placement.NormalPosition.Top,
            placement.NormalPosition.Right,
            placement.NormalPosition.Bottom);
        PixelRect normalScreenBounds = ConvertWorkspaceBoundsToScreen(
            handle,
            normalWorkspaceBounds);
        PixelRect normalBounds = ConvertRawBoundsToExtended(
            handle,
            normalScreenBounds);
        NormalWindowRestorePoint? restorePoint = normalBounds.Width > 0 && normalBounds.Height > 0
            ? new NormalWindowRestorePoint(
                handle,
                normalBounds,
                GetWindowDpi(handle),
                NormalRestorePointSource.WindowPlacement)
            : null;

        return new ManagedWindowSnapshot(
            GetExtendedBounds(handle),
            IsZoomed(handle),
            bytes,
            restorePoint);
    }

    public PixelRect GetExtendedBounds(nint handle)
    {
        if (DwmGetWindowAttribute(
                handle,
                DwmwaExtendedFrameBounds,
                out NativeRect bounds,
                Marshal.SizeOf<NativeRect>()) != 0)
        {
            _ = GetWindowRect(handle, out bounds);
        }

        return new PixelRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
    }

    public bool IsMaximized(nint handle) => IsZoomed(handle);

    public bool TryApplyNative(nint handle, PixelRect target) =>
        TrySetExtendedBounds(handle, target);

    public bool TryApplyManaged(nint handle, PixelRect target)
    {
        if (IsZoomed(handle))
        {
            _ = ShowWindow(handle, SwRestore);
        }

        return TrySetExtendedBounds(handle, target);
    }

    public bool TryRestore(nint handle, ManagedWindowSnapshot snapshot)
    {
        WindowPlacement placement = Deserialize(snapshot.NativePlacement);
        if (placement.Length == Marshal.SizeOf<WindowPlacement>()
            && SetWindowPlacement(handle, ref placement))
        {
            return true;
        }

        return TrySetExtendedBounds(handle, snapshot.ExtendedBounds);
    }

    public NormalWindowRestorePoint ResolveRestorePoint(
        nint handle,
        NormalWindowRestorePoint restorePoint)
    {
        NativeRect requested = ToNativeRect(restorePoint.Bounds);
        if (MonitorFromRect(ref requested, 0) != 0)
        {
            return restorePoint;
        }

        nint monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info))
        {
            return restorePoint;
        }

        return NormalRestorePointMapper.MapToWorkArea(
            restorePoint,
            new PixelRect(
                info.WorkArea.Left,
                info.WorkArea.Top,
                info.WorkArea.Right,
                info.WorkArea.Bottom),
            GetWindowDpi(handle));
    }

    public bool TryRestoreNormal(
        nint handle,
        ManagedWindowSnapshot snapshot,
        NormalWindowRestorePoint restorePoint)
    {
        WindowPlacement placement = Deserialize(snapshot.NativePlacement);
        if (placement.Length == Marshal.SizeOf<WindowPlacement>())
        {
            placement.ShowCommand = SwShownormal;
            _ = SetWindowPlacement(handle, ref placement);
        }

        _ = ShowWindow(handle, SwRestore);
        return TrySetExtendedBounds(handle, restorePoint.Bounds);
    }

    private static bool TrySetExtendedBounds(nint handle, PixelRect target)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            PixelRect extended = GetExtendedBoundsStatic(handle);
            if (!GetWindowRect(handle, out NativeRect raw))
            {
                raw = new NativeRect
                {
                    Left = extended.Left,
                    Top = extended.Top,
                    Right = extended.Right,
                    Bottom = extended.Bottom,
                };
            }

            int rawLeft = target.Left + (raw.Left - extended.Left);
            int rawTop = target.Top + (raw.Top - extended.Top);
            int rawRight = target.Right + (raw.Right - extended.Right);
            int rawBottom = target.Bottom + (raw.Bottom - extended.Bottom);
            if (!SetWindowPos(
                    handle,
                    0,
                    rawLeft,
                    rawTop,
                    Math.Max(1, rawRight - rawLeft),
                    Math.Max(1, rawBottom - rawTop),
                    SwpNozorder | SwpNoactivate | SwpFramechanged | SwpNoownerzorder))
            {
                return false;
            }

            PixelRect actual = GetExtendedBoundsStatic(handle);
            if (WithinOnePixel(actual, target))
            {
                return true;
            }
        }

        return WithinOnePixel(GetExtendedBoundsStatic(handle), target);
    }

    private static PixelRect GetExtendedBoundsStatic(nint handle)
    {
        if (DwmGetWindowAttribute(
                handle,
                DwmwaExtendedFrameBounds,
                out NativeRect bounds,
                Marshal.SizeOf<NativeRect>()) != 0)
        {
            _ = GetWindowRect(handle, out bounds);
        }

        return new PixelRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
    }

    private static bool WithinOnePixel(PixelRect actual, PixelRect target) =>
        Math.Abs(actual.Left - target.Left) <= 1
        && Math.Abs(actual.Top - target.Top) <= 1
        && Math.Abs(actual.Right - target.Right) <= 1
        && Math.Abs(actual.Bottom - target.Bottom) <= 1;

    private static PixelRect ConvertRawBoundsToExtended(nint handle, PixelRect rawBounds)
    {
        PixelRect currentExtended = GetExtendedBoundsStatic(handle);
        if (!GetWindowRect(handle, out NativeRect currentRaw))
        {
            return rawBounds;
        }

        return new PixelRect(
            rawBounds.Left - (currentRaw.Left - currentExtended.Left),
            rawBounds.Top - (currentRaw.Top - currentExtended.Top),
            rawBounds.Right - (currentRaw.Right - currentExtended.Right),
            rawBounds.Bottom - (currentRaw.Bottom - currentExtended.Bottom));
    }

    private static PixelRect ConvertWorkspaceBoundsToScreen(
        nint handle,
        PixelRect workspaceBounds)
    {
        nint monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info))
        {
            return workspaceBounds;
        }

        return WorkspaceBoundsConverter.ToScreen(
            workspaceBounds,
            new PixelRect(
                info.Monitor.Left,
                info.Monitor.Top,
                info.Monitor.Right,
                info.Monitor.Bottom),
            new PixelRect(
                info.WorkArea.Left,
                info.WorkArea.Top,
                info.WorkArea.Right,
                info.WorkArea.Bottom));
    }

    private static uint GetWindowDpi(nint handle)
    {
        uint dpi = GetDpiForWindow(handle);
        return dpi == 0 ? 96u : dpi;
    }

    private static NativeRect ToNativeRect(PixelRect bounds) => new()
    {
        Left = bounds.Left,
        Top = bounds.Top,
        Right = bounds.Right,
        Bottom = bounds.Bottom,
    };

    private static WindowPlacement Deserialize(byte[] bytes)
    {
        if (bytes.Length != Marshal.SizeOf<WindowPlacement>())
        {
            return default;
        }

        nint buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            return Marshal.PtrToStructure<WindowPlacement>(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCommand;
        public NativePoint MinimumPosition;
        public NativePoint MaximumPosition;
        public NativeRect NormalPosition;
        public NativeRect Device;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(nint handle, ref WindowPlacement placement);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPlacement(nint handle, ref WindowPlacement placement);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint handle, out NativeRect bounds);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint handle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint handle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint handle);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromRect(ref NativeRect bounds, uint flags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint handle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint handle,
        int attribute,
        out NativeRect value,
        int valueSize);
}
