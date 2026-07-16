using CodexUsageCompanion.Windows;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class LatestWindowFramePumpTests
{
    [Fact]
    public void BurstOfSignalsSchedulesOneFrameAndConsumesOnlyLatestValue()
    {
        var clock = new FakeWindowFrameClock();
        using var pump = new LatestWindowFramePump<int>(clock);
        var observed = new List<int>();
        pump.FrameReady += value => observed.Add(value);

        pump.Offer(1, TimeSpan.FromMilliseconds(16));
        pump.Offer(2, TimeSpan.FromMilliseconds(16));
        pump.Offer(3, TimeSpan.FromMilliseconds(16));

        Assert.Equal(1, clock.ScheduleCount);
        clock.Fire();
        Assert.Equal([3], observed);
    }

    [Fact]
    public void SignalArrivingDuringConsumerIsDeferredAndCannotLandOutOfOrder()
    {
        var clock = new FakeWindowFrameClock();
        using var pump = new LatestWindowFramePump<int>(clock);
        var observed = new List<int>();
        pump.FrameReady += value =>
        {
            observed.Add(value);
            if (value == 1)
            {
                pump.Offer(2, TimeSpan.FromMilliseconds(16));
                pump.Offer(3, TimeSpan.FromMilliseconds(16));
            }
        };

        pump.Offer(1, TimeSpan.FromMilliseconds(16));
        clock.Fire();
        Assert.Equal([1], observed);
        Assert.Equal(2, clock.ScheduleCount);

        clock.Fire();
        Assert.Equal([1, 3], observed);
    }

    [Fact]
    public void ImmediateSignalExpeditesAnAlreadyScheduledFrame()
    {
        var clock = new FakeWindowFrameClock();
        using var pump = new LatestWindowFramePump<int>(clock);
        int observed = 0;
        pump.FrameReady += value => observed = value;
        pump.Offer(1, TimeSpan.FromMilliseconds(16));

        pump.Offer(2, TimeSpan.Zero);

        Assert.Equal(TimeSpan.Zero, clock.LastDelay);
        clock.Fire();
        Assert.Equal(2, observed);
    }

    [Fact]
    public void ControlFrameCannotBeOverwrittenByFollowingCoalescedLocationFrames()
    {
        var clock = new FakeWindowFrameClock();
        using var pump = new LatestWindowFramePump<string>(clock);
        var observed = new List<string>();
        pump.FrameReady += observed.Add;

        pump.Offer("move-start", TimeSpan.Zero, coalesce: false);
        pump.Offer("location-1", TimeSpan.FromMilliseconds(16), coalesce: true);
        pump.Offer("location-2", TimeSpan.FromMilliseconds(16), coalesce: true);

        clock.Fire();
        clock.Fire();
        Assert.Equal(["move-start", "location-2"], observed);
    }

    [Fact]
    public void MoveEndQueuedAfterLocationIsDeliveredInOrder()
    {
        var clock = new FakeWindowFrameClock();
        using var pump = new LatestWindowFramePump<string>(clock);
        var observed = new List<string>();
        pump.FrameReady += observed.Add;

        pump.Offer("location", TimeSpan.FromMilliseconds(16), coalesce: true);
        pump.Offer("move-end", TimeSpan.Zero, coalesce: false);

        clock.Fire();
        clock.Fire();
        Assert.Equal(["location", "move-end"], observed);
    }

    private sealed class FakeWindowFrameClock : IWindowFrameClock
    {
        public event Action? Tick;

        public int ScheduleCount { get; private set; }

        public TimeSpan LastDelay { get; private set; }

        public bool IsHighResolution => true;

        public void Schedule(TimeSpan delay)
        {
            ScheduleCount++;
            LastDelay = delay;
        }

        public void Cancel()
        {
        }

        public void Fire() => Tick?.Invoke();

        public void Dispose()
        {
        }
    }
}
