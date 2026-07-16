namespace CodexUsageCompanion.Windows;

public sealed class LatestValueDispatchQueue<T>
{
    private readonly object _gate = new();
    private readonly List<PendingValue> _pending = [];
    private bool _consumerActive;

    public bool Offer(T value, bool coalesce = true)
    {
        lock (_gate)
        {
            var pending = new PendingValue(value, coalesce);
            if (coalesce && _pending.Count > 0 && _pending[^1].Coalescible)
            {
                _pending[^1] = pending;
            }
            else
            {
                _pending.Add(pending);
            }

            if (_consumerActive)
            {
                return false;
            }

            _consumerActive = true;
            return true;
        }
    }

    public bool TryTake(out T? value)
    {
        lock (_gate)
        {
            if (_pending.Count == 0)
            {
                _consumerActive = false;
                value = default;
                return false;
            }

            PendingValue pending = _pending[0];
            _pending.RemoveAt(0);
            value = pending.Value;
            return true;
        }
    }

    private sealed record PendingValue(T Value, bool Coalescible);
}
