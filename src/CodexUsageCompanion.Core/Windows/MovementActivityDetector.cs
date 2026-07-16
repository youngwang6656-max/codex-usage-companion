namespace CodexUsageCompanion.Windows;

public sealed class MovementActivityDetector
{
    private const long InferenceWindowMilliseconds = 80;
    private const long InactivityWindowMilliseconds = 150;
    private long _lastLocationMilliseconds = long.MinValue;

    public bool IsMoving { get; private set; }

    public bool OnOfficialMoveStarted(long nowMilliseconds)
    {
        _lastLocationMilliseconds = nowMilliseconds;
        if (IsMoving)
        {
            return false;
        }

        IsMoving = true;
        return true;
    }

    public bool OnOfficialMoveEnded(long nowMilliseconds)
    {
        _lastLocationMilliseconds = nowMilliseconds;
        if (!IsMoving)
        {
            return false;
        }

        IsMoving = false;
        return true;
    }

    public bool OnLocation(long nowMilliseconds)
    {
        long previous = _lastLocationMilliseconds;
        _lastLocationMilliseconds = nowMilliseconds;
        if (IsMoving || previous == long.MinValue)
        {
            return false;
        }

        long elapsed = nowMilliseconds - previous;
        if (elapsed < 0 || elapsed > InferenceWindowMilliseconds)
        {
            return false;
        }

        IsMoving = true;
        return true;
    }

    public bool TryEndForInactivity(long nowMilliseconds)
    {
        if (!IsMoving || _lastLocationMilliseconds == long.MinValue)
        {
            return false;
        }

        if (nowMilliseconds - _lastLocationMilliseconds < InactivityWindowMilliseconds)
        {
            return false;
        }

        IsMoving = false;
        return true;
    }
}
