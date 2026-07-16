namespace CodexUsageCompanion.Windows;

public sealed class CodexWindowTracker : ICodexWindowTracker
{
    private readonly IWindowSnapshotSource _source;
    private readonly object _gate = new();
    private bool _started;
    private bool _disposed;

    public CodexWindowTracker(IWindowSnapshotSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public event EventHandler<CodexWindowState?>? WindowChanged;

    public CodexWindowState? Current { get; private set; }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        _source.Changed += OnSourceChanged;
        _source.Start();
        _started = true;
        Refresh(new WindowChangeSignal(0, WindowChangeKind.Poll, 0));
    }

    public PixelRect GetWorkArea(nint windowHandle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _source.GetWorkArea(windowHandle);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source.Changed -= OnSourceChanged;
        _source.Dispose();
    }

    private void OnSourceChanged(object? sender, WindowChangeSignal signal) => Refresh(signal);

    private void Refresh(WindowChangeSignal signal)
    {
        CodexWindowState? current;
        lock (_gate)
        {
            current = Current;
        }

        bool mayUseDirectSample = current is not null
            && signal.Handle == current.Handle
            && signal.Kind is WindowChangeKind.Location
                or WindowChangeKind.MoveSizeStarted
                or WindowChangeKind.MoveSizeEnded
                or WindowChangeKind.Visibility;
        CodexWindowState? next = mayUseDirectSample
            && _source.TryGetCandidate(signal.Handle, out CodexWindowCandidate candidate)
                ? CodexWindowSelector.Select([candidate], signal.Handle)
                : CodexWindowSelector.Select(
                    _source.GetCandidates(),
                    _source.GetForegroundWindow());
        if (next is not null)
        {
            next = next with { ChangeKind = signal.Kind };
        }

        EventHandler<CodexWindowState?>? handler;
        lock (_gate)
        {
            bool forcePublish = signal.Kind is WindowChangeKind.Foreground
                or WindowChangeKind.MoveSizeStarted
                or WindowChangeKind.MoveSizeEnded;
            if (!forcePublish && SnapshotsEqual(Current, next))
            {
                return;
            }

            Current = next;
            handler = WindowChanged;
        }

        handler?.Invoke(this, next);
    }

    private static bool SnapshotsEqual(CodexWindowState? left, CodexWindowState? right) =>
        Equals(
            left is null ? null : left with { ChangeKind = WindowChangeKind.Poll },
            right is null ? null : right with { ChangeKind = WindowChangeKind.Poll });
}
