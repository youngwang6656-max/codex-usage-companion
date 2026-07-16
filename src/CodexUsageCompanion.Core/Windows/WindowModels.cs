namespace CodexUsageCompanion.Windows;

public enum WindowChangeKind
{
    Poll,
    Foreground,
    MoveSizeStarted,
    MoveSizeEnded,
    Location,
    Visibility,
    Destroyed,
}

public sealed record WindowChangeSignal(
    nint Handle,
    WindowChangeKind Kind,
    uint EventTime);

public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Math.Max(0, Right - Left);

    public int Height => Math.Max(0, Bottom - Top);
}

public sealed record CodexWindowState(
    nint Handle,
    PixelRect Bounds,
    uint Dpi,
    bool IsVisible,
    bool IsMinimized,
    string ExecutablePath,
    bool IsMaximized = false,
    WindowChangeKind ChangeKind = WindowChangeKind.Poll);

public sealed record CodexWindowCandidate(
    nint Handle,
    string? ExecutablePath,
    string Title,
    PixelRect Bounds,
    uint Dpi,
    bool IsVisible,
    bool IsMinimized,
    bool IsCloaked,
    uint ProcessId = 0,
    bool IsMaximized = false,
    bool IsToolWindow = false);

public enum CompanionLayoutMode
{
    BottomAttached,
    NativeIntegratedFullscreen,
    ManagedIntegratedFullscreen,
    FullscreenOverlay,
}

public readonly record struct CompanionPlacement(
    int Left,
    int Top,
    int Width,
    int Height,
    CompanionLayoutMode Mode)
{
    public bool PlacedInsideCodex =>
        Mode is CompanionLayoutMode.NativeIntegratedFullscreen
            or CompanionLayoutMode.ManagedIntegratedFullscreen
            or CompanionLayoutMode.FullscreenOverlay;
}

public readonly record struct CompanionLayoutDecision(
    CompanionPlacement Companion,
    PixelRect? CodexTarget);
