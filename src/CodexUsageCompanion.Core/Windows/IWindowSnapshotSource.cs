namespace CodexUsageCompanion.Windows;

public interface IWindowSnapshotSource : IDisposable
{
    event EventHandler<WindowChangeSignal>? Changed;

    void Start();

    IReadOnlyList<CodexWindowCandidate> GetCandidates();

    bool TryGetCandidate(nint handle, out CodexWindowCandidate candidate);

    nint GetForegroundWindow();

    PixelRect GetWorkArea(nint windowHandle);
}

public interface ICodexWindowTracker : IDisposable
{
    event EventHandler<CodexWindowState?>? WindowChanged;

    CodexWindowState? Current { get; }

    void Start();

    PixelRect GetWorkArea(nint windowHandle);
}
