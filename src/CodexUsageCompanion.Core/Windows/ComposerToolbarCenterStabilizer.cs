namespace CodexUsageCompanion.Windows;

public sealed class ComposerToolbarCenterStabilizer
{
    private readonly object _gate = new();
    private StableEntry? _stable;

    public int Observe(PixelRect windowBounds, uint dpi, int centerY)
    {
        lock (_gate)
        {
            uint normalizedDpi = dpi == 0 ? 96u : dpi;
            if (_stable is not { } stable
                || stable.Dpi != normalizedDpi)
            {
                _stable = new StableEntry(
                    normalizedDpi,
                    windowBounds.Bottom - centerY);
                return centerY;
            }

            return windowBounds.Bottom - stable.BottomOffset;
        }
    }

    public bool TryResolve(
        PixelRect windowBounds,
        uint dpi,
        out int centerY)
    {
        lock (_gate)
        {
            uint normalizedDpi = dpi == 0 ? 96u : dpi;
            if (_stable is not { } stable
                || stable.Dpi != normalizedDpi)
            {
                centerY = 0;
                return false;
            }

            centerY = windowBounds.Bottom - stable.BottomOffset;
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _stable = null;
        }
    }

    private sealed record StableEntry(
        uint Dpi,
        int BottomOffset);
}
