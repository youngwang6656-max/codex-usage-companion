using System.Windows.Automation;
using CodexUsageCompanion.Windows;

namespace CodexUsageCompanion;

internal readonly record struct ModelSelectorBoundsUpdate(
    nint Handle,
    long Generation,
    PixelRect WindowBounds,
    uint Dpi,
    PixelRect SelectorBounds,
    double Confidence);

internal interface IModelSelectorBoundsProvider : IDisposable
{
    event Action<ModelSelectorBoundsUpdate>? BoundsResolved;

    void Request(CodexWindowState window, long generation);
}

internal sealed class UiaModelSelectorBoundsProvider : IModelSelectorBoundsProvider
{
    private readonly LatestValueDispatchQueue<RequestState> _requests = new();
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;

    public event Action<ModelSelectorBoundsUpdate>? BoundsResolved;

    public void Request(CodexWindowState window, long generation)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (_requests.Offer(new RequestState(window, generation)))
        {
            _ = Task.Run(DrainRequests);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private void DrainRequests()
    {
        while (!_lifetime.IsCancellationRequested
            && _requests.TryTake(out RequestState? request)
            && request is { } current)
        {
            try
            {
                if (TryResolve(current.Window, out PixelRect bounds, out double confidence))
                {
                    BoundsResolved?.Invoke(new ModelSelectorBoundsUpdate(
                        current.Window.Handle,
                        current.Generation,
                        current.Window.Bounds,
                        current.Window.Dpi,
                        bounds,
                        confidence));
                }
            }
            catch (ElementNotAvailableException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.Runtime.InteropServices.COMException)
            {
            }
        }
    }

    private static bool TryResolve(
        CodexWindowState window,
        out PixelRect bounds,
        out double confidence)
    {
        AutomationElement root = AutomationElement.FromHandle(window.Handle);
        var controlCondition = new OrCondition(
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Button),
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.ComboBox));
        var cache = new CacheRequest
        {
            AutomationElementMode = AutomationElementMode.Full,
            TreeScope = TreeScope.Element,
        };
        cache.Add(AutomationElement.BoundingRectangleProperty);
        cache.Add(AutomationElement.ControlTypeProperty);
        cache.Add(AutomationElement.IsOffscreenProperty);
        cache.Add(AutomationElement.NameProperty);
        cache.Add(ExpandCollapsePattern.Pattern);

        AutomationElementCollection elements;
        using (cache.Activate())
        {
            elements = root.FindAll(TreeScope.Descendants, controlCondition);
        }

        var candidates = new List<ModelSelectorCandidate>(elements.Count);
        foreach (AutomationElement element in elements)
        {
            if (element.Cached.IsOffscreen)
            {
                continue;
            }

            System.Windows.Rect rectangle = element.Cached.BoundingRectangle;
            if (rectangle.IsEmpty
                || double.IsNaN(rectangle.Left)
                || double.IsNaN(rectangle.Top)
                || double.IsInfinity(rectangle.Left)
                || double.IsInfinity(rectangle.Top))
            {
                continue;
            }

            ControlType type = element.Cached.ControlType;
            bool hasExpandCollapse = element.TryGetCachedPattern(
                ExpandCollapsePattern.Pattern,
                out _);
            candidates.Add(new ModelSelectorCandidate(
                ToPixelRect(rectangle),
                type == ControlType.ComboBox
                    ? AutomationControlKind.ComboBox
                    : AutomationControlKind.Button,
                hasExpandCollapse,
                element.Cached.Name));
        }

        if (ModelSelectorCandidateScorer.TrySelect(
                window.Bounds,
                window.Dpi,
                candidates,
                out ModelSelectorCandidate selected,
                out confidence))
        {
            bounds = selected.Bounds;
            return true;
        }

        bounds = default;
        return false;
    }

    private static PixelRect ToPixelRect(System.Windows.Rect rectangle) =>
        new(
            (int)Math.Round(rectangle.Left, MidpointRounding.AwayFromZero),
            (int)Math.Round(rectangle.Top, MidpointRounding.AwayFromZero),
            (int)Math.Round(rectangle.Right, MidpointRounding.AwayFromZero),
            (int)Math.Round(rectangle.Bottom, MidpointRounding.AwayFromZero));

    private sealed record RequestState(CodexWindowState Window, long Generation);
}
