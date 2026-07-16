using CodexUsageCompanion.Windows;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class MovementActivityDetectorTests
{
    [Fact]
    public void OfficialMoveStartImmediatelyEntersMovingState()
    {
        var detector = new MovementActivityDetector();

        bool started = detector.OnOfficialMoveStarted(nowMilliseconds: 1_000);

        Assert.True(started);
        Assert.True(detector.IsMoving);
    }

    [Fact]
    public void TwoLocationEventsWithinEightyMillisecondsInferMovement()
    {
        var detector = new MovementActivityDetector();

        Assert.False(detector.OnLocation(nowMilliseconds: 1_000));
        Assert.True(detector.OnLocation(nowMilliseconds: 1_080));

        Assert.True(detector.IsMoving);
    }

    [Fact]
    public void WidelySeparatedLocationEventsDoNotInferMovement()
    {
        var detector = new MovementActivityDetector();

        Assert.False(detector.OnLocation(nowMilliseconds: 1_000));
        Assert.False(detector.OnLocation(nowMilliseconds: 1_081));

        Assert.False(detector.IsMoving);
    }

    [Fact]
    public void InactivityAtOneHundredFiftyMillisecondsEndsMovementAndRequestsCalibration()
    {
        var detector = new MovementActivityDetector();
        _ = detector.OnLocation(nowMilliseconds: 1_000);
        _ = detector.OnLocation(nowMilliseconds: 1_040);
        _ = detector.OnLocation(nowMilliseconds: 1_100);

        Assert.False(detector.TryEndForInactivity(nowMilliseconds: 1_249));
        Assert.True(detector.TryEndForInactivity(nowMilliseconds: 1_250));
        Assert.False(detector.IsMoving);
    }

    [Fact]
    public void OfficialMoveEndImmediatelyEndsMovement()
    {
        var detector = new MovementActivityDetector();
        detector.OnOfficialMoveStarted(nowMilliseconds: 1_000);

        bool ended = detector.OnOfficialMoveEnded(nowMilliseconds: 1_020);

        Assert.True(ended);
        Assert.False(detector.IsMoving);
    }
}
