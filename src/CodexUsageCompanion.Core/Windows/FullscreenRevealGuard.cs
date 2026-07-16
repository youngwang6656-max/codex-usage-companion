namespace CodexUsageCompanion.Windows;

public enum FullscreenRevealFailureReason
{
    GeometryDeviation,
    ConsecutiveSlowFrames,
    LateFrameRatio,
    Timeout,
    UserInterrupted,
}

public sealed class FullscreenRevealGuard
{
    private static readonly TimeSpan SlowFrameThreshold = TimeSpan.FromMilliseconds(33);
    private static readonly TimeSpan TimeoutThreshold = TimeSpan.FromMilliseconds(350);
    private const int MinimumRatioSample = 8;
    private int _frames;
    private int _lateFrames;
    private int _consecutiveSlowFrames;
    private FullscreenRevealFailureReason? _failure;

    public FullscreenRevealFailureReason? ReportFrame(
        TimeSpan frameInterval,
        int geometryDeviationPixels)
    {
        if (_failure is not null)
        {
            return _failure;
        }

        if (geometryDeviationPixels > 2)
        {
            return Fail(FullscreenRevealFailureReason.GeometryDeviation);
        }

        _frames++;
        if (frameInterval > SlowFrameThreshold)
        {
            _lateFrames++;
            _consecutiveSlowFrames++;
        }
        else
        {
            _consecutiveSlowFrames = 0;
        }

        if (_consecutiveSlowFrames >= 3)
        {
            return Fail(FullscreenRevealFailureReason.ConsecutiveSlowFrames);
        }

        if (_frames >= MinimumRatioSample
            && (double)_lateFrames / _frames > 0.25)
        {
            return Fail(FullscreenRevealFailureReason.LateFrameRatio);
        }

        return null;
    }

    public FullscreenRevealFailureReason? CheckElapsed(TimeSpan elapsed) =>
        elapsed > TimeoutThreshold
            ? Fail(FullscreenRevealFailureReason.Timeout)
            : _failure;

    public FullscreenRevealFailureReason Interrupt() =>
        Fail(FullscreenRevealFailureReason.UserInterrupted);

    private FullscreenRevealFailureReason Fail(FullscreenRevealFailureReason reason)
    {
        _failure ??= reason;
        return _failure.Value;
    }
}
