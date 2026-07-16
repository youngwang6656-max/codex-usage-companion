namespace CodexUsageCompanion.Windows;

public sealed class AdaptiveFollowRateController
{
    private static readonly int[] MovingFpsLevels = [60, 30, 20, 15];
    private int _movingLevel;
    private int _stableSamples;
    private bool _isMoving;

    public int CurrentFps => _isMoving ? MovingFpsLevels[_movingLevel] : 30;

    public TimeSpan FrameInterval => TimeSpan.FromSeconds(1d / CurrentFps);

    public void OnMoveSizeStarted()
    {
        _isMoving = true;
        _movingLevel = 0;
        _stableSamples = 0;
    }

    public void OnMoveSizeEnded()
    {
        _isMoving = false;
        _stableSamples = 0;
    }

    public void Report(double cpuPercent, double missedFrameRatio)
    {
        if (!_isMoving)
        {
            return;
        }

        if (cpuPercent > 5 || missedFrameRatio > 0.25)
        {
            _movingLevel = Math.Min(_movingLevel + 1, MovingFpsLevels.Length - 1);
            _stableSamples = 0;
            return;
        }

        if (cpuPercent < 2 && missedFrameRatio <= 0.05)
        {
            _stableSamples++;
            if (_stableSamples >= 2)
            {
                _movingLevel = Math.Max(0, _movingLevel - 1);
                _stableSamples = 0;
            }

            return;
        }

        _stableSamples = 0;
    }
}
