using CodexUsageCompanion.Windows;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class CodexWindowTrackerTests
{
    [Fact]
    public void Start_PublishesCurrentCodexWindow()
    {
        var source = new FakeWindowSource
        {
            Candidates = [Candidate((nint)4)],
            ForegroundWindow = (nint)4,
        };
        using var tracker = new CodexWindowTracker(source);
        CodexWindowState? observed = null;
        tracker.WindowChanged += (_, state) => observed = state;

        tracker.Start();

        Assert.Equal((nint)4, observed!.Handle);
        Assert.Equal(observed, tracker.Current);
    }

    [Fact]
    public void SourceChange_DoesNotPublishDuplicateSnapshot()
    {
        var source = new FakeWindowSource
        {
            Candidates = [Candidate((nint)4)],
            ForegroundWindow = (nint)4,
        };
        using var tracker = new CodexWindowTracker(source);
        int changes = 0;
        tracker.WindowChanged += (_, _) => changes++;
        tracker.Start();

        source.RaiseChanged();

        Assert.Equal(1, changes);
    }

    [Fact]
    public void MoveSizeSignals_ArePublishedEvenWhenBoundsHaveNotChanged()
    {
        var source = new FakeWindowSource
        {
            Candidates = [Candidate((nint)4)],
            ForegroundWindow = (nint)4,
        };
        using var tracker = new CodexWindowTracker(source);
        var kinds = new List<WindowChangeKind>();
        tracker.WindowChanged += (_, state) => kinds.Add(state!.ChangeKind);
        tracker.Start();

        source.RaiseChanged(WindowChangeKind.MoveSizeStarted);
        source.RaiseChanged(WindowChangeKind.MoveSizeEnded);

        Assert.Equal(
            [WindowChangeKind.Poll, WindowChangeKind.MoveSizeStarted, WindowChangeKind.MoveSizeEnded],
            kinds);
    }

    [Fact]
    public void ForegroundSignal_IsPublishedSoUsageCanRefreshImmediately()
    {
        var source = new FakeWindowSource
        {
            Candidates = [Candidate((nint)4)],
            ForegroundWindow = (nint)4,
        };
        using var tracker = new CodexWindowTracker(source);
        var kinds = new List<WindowChangeKind>();
        tracker.WindowChanged += (_, state) => kinds.Add(state!.ChangeKind);
        tracker.Start();

        source.RaiseChanged(WindowChangeKind.Foreground);

        Assert.Equal([WindowChangeKind.Poll, WindowChangeKind.Foreground], kinds);
    }

    [Fact]
    public void SourceChange_PublishesMinimizeAndCloseTransitions()
    {
        var source = new FakeWindowSource
        {
            Candidates = [Candidate((nint)4)],
            ForegroundWindow = (nint)4,
        };
        using var tracker = new CodexWindowTracker(source);
        var states = new List<CodexWindowState?>();
        tracker.WindowChanged += (_, state) => states.Add(state);
        tracker.Start();

        source.Candidates = [Candidate((nint)4) with { IsMinimized = true }];
        source.RaiseChanged();
        source.Candidates = [];
        source.RaiseChanged();

        Assert.Collection(
            states,
            state => Assert.False(state!.IsMinimized),
            state => Assert.True(state!.IsMinimized),
            Assert.Null);
    }

    [Fact]
    public void LocationSignal_ForCurrentHandleUsesFastSampleWithoutEnumeratingAllWindows()
    {
        var source = new FakeWindowSource
        {
            Candidates = [Candidate((nint)4)],
            ForegroundWindow = (nint)4,
        };
        using var tracker = new CodexWindowTracker(source);
        tracker.Start();
        source.DirectCandidate = Candidate((nint)4) with
        {
            Bounds = new PixelRect(180, 140, 1080, 740),
        };

        source.RaiseChanged(WindowChangeKind.Location, (nint)4);

        Assert.Equal(1, source.EnumerationCount);
        Assert.Equal(1, source.DirectSampleCount);
        Assert.Equal(source.DirectCandidate.Bounds, tracker.Current!.Bounds);
    }

    [Theory]
    [InlineData(WindowChangeKind.MoveSizeStarted)]
    [InlineData(WindowChangeKind.MoveSizeEnded)]
    public void MoveSignals_ForCurrentHandleUseFastSample(WindowChangeKind kind)
    {
        var source = new FakeWindowSource
        {
            Candidates = [Candidate((nint)4)],
            ForegroundWindow = (nint)4,
        };
        using var tracker = new CodexWindowTracker(source);
        tracker.Start();
        source.DirectCandidate = Candidate((nint)4);

        source.RaiseChanged(kind, (nint)4);

        Assert.Equal(1, source.EnumerationCount);
        Assert.Equal(1, source.DirectSampleCount);
    }

    [Fact]
    public void ForegroundSignalStillEnumeratesToSelectTheActiveCodexWindow()
    {
        var source = new FakeWindowSource
        {
            Candidates = [Candidate((nint)4)],
            ForegroundWindow = (nint)4,
        };
        using var tracker = new CodexWindowTracker(source);
        tracker.Start();

        source.RaiseChanged(WindowChangeKind.Foreground, (nint)9);

        Assert.Equal(2, source.EnumerationCount);
        Assert.Equal(0, source.DirectSampleCount);
    }

    [Fact]
    public void VisibilitySignal_ForCurrentHandleUsesDirectSample()
    {
        var source = new FakeWindowSource
        {
            Candidates = [Candidate((nint)4)],
            ForegroundWindow = (nint)4,
        };
        using var tracker = new CodexWindowTracker(source);
        tracker.Start();
        source.DirectCandidate = Candidate((nint)4) with { IsVisible = false };

        source.RaiseChanged(WindowChangeKind.Visibility, (nint)4);

        Assert.Equal(1, source.EnumerationCount);
        Assert.Equal(1, source.DirectSampleCount);
        Assert.False(tracker.Current!.IsVisible);
    }

    private static CodexWindowCandidate Candidate(nint handle) => new(
        handle,
        @"C:\Program Files\WindowsApps\OpenAI.Codex_1\app\ChatGPT.exe",
        "Codex",
        new PixelRect(100, 100, 1000, 700),
        Dpi: 96,
        IsVisible: true,
        IsMinimized: false,
        IsCloaked: false);

    private sealed class FakeWindowSource : IWindowSnapshotSource
    {
        public event EventHandler<WindowChangeSignal>? Changed;

        public IReadOnlyList<CodexWindowCandidate> Candidates { get; set; } = [];

        public nint ForegroundWindow { get; set; }

        public CodexWindowCandidate? DirectCandidate { get; set; }

        public int EnumerationCount { get; private set; }

        public int DirectSampleCount { get; private set; }

        public IReadOnlyList<CodexWindowCandidate> GetCandidates()
        {
            EnumerationCount++;
            return Candidates;
        }

        public bool TryGetCandidate(nint handle, out CodexWindowCandidate candidate)
        {
            DirectSampleCount++;
            if (DirectCandidate is not null && DirectCandidate.Handle == handle)
            {
                candidate = DirectCandidate;
                return true;
            }

            candidate = default!;
            return false;
        }

        public nint GetForegroundWindow() => ForegroundWindow;

        public PixelRect GetWorkArea(nint windowHandle) => new(0, 0, 1920, 1080);

        public void Start()
        {
        }

        public void RaiseChanged(
            WindowChangeKind kind = WindowChangeKind.Location,
            nint handle = 0) => Changed?.Invoke(
                this,
                new WindowChangeSignal(handle, kind, EventTime: 0));

        public void Dispose()
        {
        }
    }
}
