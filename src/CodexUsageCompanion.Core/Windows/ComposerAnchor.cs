namespace CodexUsageCompanion.Windows;

public enum ComposerAnchorSource
{
    Automation,
    CachedAutomation,
    ComposerFallback,
    FixedFallback,
}

public readonly record struct ComposerAnchor(
    PixelRect ButtonBounds,
    double Confidence,
    ComposerAnchorSource Source);

public readonly record struct ComposerAnchorUpdate(
    nint Handle,
    uint Dpi,
    PixelRect WindowBounds,
    ComposerAnchor Anchor);

public readonly record struct ComposerCapture(
    byte[] BgraPixels,
    int Width,
    int Height,
    int Stride,
    PixelRect ScreenBounds,
    uint Dpi);

public readonly record struct ComposerLandmarks(
    int SendCenterX,
    int SendCenterY,
    int SendRadius,
    int ScrollbarLeft,
    double Confidence);

public enum AutomationControlKind
{
    Other,
    Button,
    ComboBox,
}

public readonly record struct ModelSelectorCandidate(
    PixelRect Bounds,
    AutomationControlKind ControlKind,
    bool SupportsExpandCollapse,
    string? Name);

public static class ComposerAnchorPlacement
{
    private const double ButtonSizeDip = 28;
    private const double ModelSelectorGapDip = 10;
    private const double ModelSelectorLeftFromSendCenterDip = 294;
    private const double MinimumFixedButtonRightInsetDip = 220;
    private const double ResponsiveComposerWidthOffsetDip = 634;
    private const double FixedButtonBottomInsetDip = 38;
    private const double FullscreenLogicalWidthDip = 1400;
    private const double FullscreenLogicalHeightDip = 900;
    private const double FullscreenFixedButtonBottomInsetDip = 44.5;
    private const double SafeInsetDip = 12;

    public static ComposerAnchor CreateForModelSelector(
        PixelRect selectorBounds,
        PixelRect windowBounds,
        uint dpi,
        double confidence)
        => CreateForModelSelector(
            selectorBounds,
            windowBounds,
            dpi,
            confidence,
            selectorBounds.Top + (selectorBounds.Height / 2));

    public static ComposerAnchor CreateForModelSelector(
        PixelRect selectorBounds,
        PixelRect windowBounds,
        uint dpi,
        double confidence,
        int rowCenterY)
    {
        int right = selectorBounds.Left - Scale(ModelSelectorGapDip, dpi);
        return CreateAtRightEdge(
            right,
            rowCenterY,
            windowBounds,
            dpi,
            confidence,
            ComposerAnchorSource.Automation);
    }

    public static ComposerAnchor CreateFromComposerLandmarks(
        PixelRect windowBounds,
        ComposerLandmarks landmarks,
        uint dpi)
    {
        int selectorLeft = landmarks.SendCenterX
            - Scale(ModelSelectorLeftFromSendCenterDip, dpi);
        int right = selectorLeft - Scale(ModelSelectorGapDip, dpi);
        return CreateAtRightEdge(
            right,
            landmarks.SendCenterY,
            windowBounds,
            dpi,
            landmarks.Confidence,
            ComposerAnchorSource.ComposerFallback);
    }

    public static ComposerAnchor CreateFallback(PixelRect windowBounds, uint dpi)
    {
        uint normalizedDpi = Math.Max(dpi, 96u);
        double scale = normalizedDpi / 96d;
        double logicalWindowWidth = windowBounds.Width / scale;
        double logicalWindowHeight = windowBounds.Height / scale;
        double rightInset = Math.Max(
            MinimumFixedButtonRightInsetDip,
            (logicalWindowWidth - ResponsiveComposerWidthOffsetDip) / 2d);
        bool useFullscreenVerticalInset =
            logicalWindowWidth >= FullscreenLogicalWidthDip
            && logicalWindowHeight >= FullscreenLogicalHeightDip;
        double bottomInset = useFullscreenVerticalInset
            ? FullscreenFixedButtonBottomInsetDip
            : FixedButtonBottomInsetDip;
        int centerY = windowBounds.Bottom - Scale(bottomInset, dpi);
        int right = windowBounds.Right - Scale(rightInset, dpi);
        return CreateAtRightEdge(
            right,
            centerY,
            windowBounds,
            dpi,
            confidence: 0,
            ComposerAnchorSource.FixedFallback);
    }

    public static int Scale(double value, uint dpi) =>
        (int)Math.Round(
            value * Math.Max(dpi, 96u) / 96d,
            MidpointRounding.AwayFromZero);

    private static ComposerAnchor CreateAtRightEdge(
        int requestedRight,
        int centerY,
        PixelRect windowBounds,
        uint dpi,
        double confidence,
        ComposerAnchorSource source)
    {
        int size = Scale(ButtonSizeDip, dpi);
        int safeInset = Scale(SafeInsetDip, dpi);
        int minimumLeft = windowBounds.Left + safeInset;
        int maximumLeft = Math.Max(
            minimumLeft,
            windowBounds.Right - safeInset - size);
        int left = Math.Clamp(requestedRight - size, minimumLeft, maximumLeft);
        int minimumTop = windowBounds.Top + safeInset;
        int maximumTop = Math.Max(
            minimumTop,
            windowBounds.Bottom - safeInset - size);
        int top = Math.Clamp(centerY - (size / 2), minimumTop, maximumTop);
        return new ComposerAnchor(
            new PixelRect(left, top, left + size, top + size),
            Math.Clamp(confidence, 0, 1),
            source);
    }
}

public sealed class ComposerToolbarCenterCache
{
    private readonly object _gate = new();
    private CacheEntry? _entry;

    public void Store(PixelRect windowBounds, uint dpi, int centerY)
    {
        lock (_gate)
        {
            _entry = new CacheEntry(
                windowBounds.Width,
                windowBounds.Height,
                dpi == 0 ? 96u : dpi,
                centerY - windowBounds.Top);
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
            if (_entry is not { } entry
                || entry.WindowWidth != windowBounds.Width
                || entry.WindowHeight != windowBounds.Height
                || entry.Dpi != normalizedDpi)
            {
                centerY = 0;
                return false;
            }

            centerY = windowBounds.Top + entry.TopOffset;
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entry = null;
        }
    }

    private sealed record CacheEntry(
        int WindowWidth,
        int WindowHeight,
        uint Dpi,
        int TopOffset);
}

public static class ModelSelectorCandidateScorer
{
    private static readonly string[] ModelTokens =
    [
        "codex",
        "gpt",
        "sol",
        "sonnet",
        "opus",
        "gemini",
        "auto",
        "low",
        "medium",
        "high",
        "低",
        "中",
        "高",
    ];

    public static bool TrySelect(
        PixelRect windowBounds,
        uint dpi,
        IEnumerable<ModelSelectorCandidate> candidates,
        out ModelSelectorCandidate selected,
        out double confidence)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        selected = default;
        confidence = 0;
        foreach (ModelSelectorCandidate candidate in candidates)
        {
            double score = Score(windowBounds, dpi, candidate);
            if (score > confidence)
            {
                selected = candidate;
                confidence = score;
            }
        }

        return confidence >= 0.75;
    }

    private static double Score(
        PixelRect windowBounds,
        uint dpi,
        ModelSelectorCandidate candidate)
    {
        PixelRect bounds = candidate.Bounds;
        int composerTop = windowBounds.Bottom
            - ComposerAnchorPlacement.Scale(180, dpi);
        int rightExclusion = windowBounds.Right
            - ComposerAnchorPlacement.Scale(120, dpi);
        int minimumWidth = ComposerAnchorPlacement.Scale(48, dpi);
        int maximumWidth = ComposerAnchorPlacement.Scale(280, dpi);
        int minimumHeight = ComposerAnchorPlacement.Scale(20, dpi);
        int maximumHeight = ComposerAnchorPlacement.Scale(52, dpi);
        bool hasName = !string.IsNullOrWhiteSpace(candidate.Name);
        bool hasModelToken = hasName && ModelTokens.Any(
            token => candidate.Name!.Contains(
                token,
                StringComparison.OrdinalIgnoreCase));
        if (candidate.ControlKind is not (
                AutomationControlKind.Button
                or AutomationControlKind.ComboBox)
            || (!candidate.SupportsExpandCollapse && !hasModelToken)
            || bounds.Width < minimumWidth
            || bounds.Width > maximumWidth
            || bounds.Height < minimumHeight
            || bounds.Height > maximumHeight
            || bounds.Top < composerTop
            || bounds.Bottom > windowBounds.Bottom
            || bounds.Left < windowBounds.Left
            || bounds.Right > windowBounds.Right
            || bounds.Left >= rightExclusion)
        {
            return 0;
        }

        double score = 0.65;
        if (candidate.SupportsExpandCollapse)
        {
            score += 0.15;
        }

        if (hasName)
        {
            score += 0.05;
        }

        if (hasModelToken)
        {
            score += 0.15;
        }

        if (candidate.ControlKind == AutomationControlKind.ComboBox)
        {
            score += 0.02;
        }

        return Math.Min(0.98, score);
    }
}

public sealed class ComposerAnchorCache
{
    private readonly object _gate = new();
    private CacheEntry? _entry;

    public void Store(PixelRect windowBounds, uint dpi, ComposerAnchor anchor)
    {
        lock (_gate)
        {
            _entry = new CacheEntry(
                dpi == 0 ? 96u : dpi,
                windowBounds.Right - anchor.ButtonBounds.Right,
                windowBounds.Bottom - anchor.ButtonBounds.Bottom,
                anchor.ButtonBounds.Width,
                anchor.ButtonBounds.Height,
                anchor.Confidence,
                anchor.Source);
        }
    }

    public bool TryResolve(
        PixelRect windowBounds,
        uint dpi,
        out ComposerAnchor anchor)
    {
        lock (_gate)
        {
            uint normalizedDpi = dpi == 0 ? 96u : dpi;
            if (_entry is not { } entry
                || entry.Dpi != normalizedDpi)
            {
                anchor = default;
                return false;
            }

            int safeInset = ComposerAnchorPlacement.Scale(12, normalizedDpi);
            int requestedRight = windowBounds.Right - entry.RightOffset;
            int requestedBottom = windowBounds.Bottom - entry.BottomOffset;
            int left = Math.Clamp(
                requestedRight - entry.Width,
                windowBounds.Left + safeInset,
                Math.Max(
                    windowBounds.Left + safeInset,
                    windowBounds.Right - safeInset - entry.Width));
            int top = Math.Clamp(
                requestedBottom - entry.Height,
                windowBounds.Top + safeInset,
                Math.Max(
                    windowBounds.Top + safeInset,
                    windowBounds.Bottom - safeInset - entry.Height));
            anchor = new ComposerAnchor(
                new PixelRect(
                    left,
                    top,
                    left + entry.Width,
                    top + entry.Height),
                entry.Confidence,
                entry.Source is ComposerAnchorSource.Automation
                    or ComposerAnchorSource.CachedAutomation
                    ? ComposerAnchorSource.CachedAutomation
                    : entry.Source);
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entry = null;
        }
    }

    private sealed record CacheEntry(
        uint Dpi,
        int RightOffset,
        int BottomOffset,
        int Width,
        int Height,
        double Confidence,
        ComposerAnchorSource Source);
}

public static class ComposerAnchorDetector
{
    private const double MinimumConfidence = 0.65;

    public static bool TryDetectLandmarks(
        ComposerCapture capture,
        out ComposerLandmarks landmarks)
    {
        if (!IsValid(capture))
        {
            landmarks = default;
            return false;
        }

        byte background = EstimateBackground(capture);
        if (!TryFindScrollbar(capture, background, out int scrollbarLeft))
        {
            landmarks = default;
            return false;
        }

        if (!TryFindSendButton(
                capture,
                background,
                scrollbarLeft,
                out CircleCandidate send))
        {
            landmarks = default;
            return false;
        }
        double confidence = Math.Clamp(
            0.55 + (send.Contrast / 400d),
            MinimumConfidence,
            0.98);
        landmarks = new ComposerLandmarks(
            capture.ScreenBounds.Left + send.CenterX,
            capture.ScreenBounds.Top + send.CenterY,
            send.Radius,
            capture.ScreenBounds.Left + scrollbarLeft,
            confidence);
        return true;
    }

    private static bool IsValid(ComposerCapture capture) =>
        capture.Width > 0
        && capture.Height > 0
        && capture.Stride >= capture.Width * 4
        && capture.BgraPixels.Length >= capture.Stride * capture.Height
        && capture.ScreenBounds.Width == capture.Width
        && capture.ScreenBounds.Height == capture.Height;

    private static byte EstimateBackground(ComposerCapture capture)
    {
        long sum = 0;
        int samples = 0;
        int maxX = Math.Max(1, capture.Width * 3 / 4);
        for (int y = 0; y < capture.Height; y += 8)
        {
            for (int x = 0; x < maxX; x += 8)
            {
                sum += GetLuminance(capture, x, y);
                samples++;
            }
        }

        return samples == 0 ? (byte)0 : (byte)(sum / samples);
    }

    private static bool TryFindScrollbar(
        ComposerCapture capture,
        byte background,
        out int scrollbarLeft)
    {
        int searchLeft = capture.Width * 3 / 4;
        int threshold = Math.Max(18, capture.Height / 4);
        int bestCount = 0;
        int bestX = -1;
        for (int x = searchLeft; x < capture.Width; x++)
        {
            int count = 0;
            for (int y = capture.Height / 5; y < capture.Height * 19 / 20; y++)
            {
                if (Math.Abs(GetLuminance(capture, x, y) - background) >= 24)
                {
                    count++;
                }
            }

            if (count > bestCount)
            {
                bestCount = count;
                bestX = x;
            }
        }

        if (bestCount < threshold || bestX < 0)
        {
            scrollbarLeft = 0;
            return false;
        }

        scrollbarLeft = bestX;
        while (scrollbarLeft > searchLeft
            && ColumnContrastCount(capture, scrollbarLeft - 1, background) >= threshold)
        {
            scrollbarLeft--;
        }

        return true;
    }

    private static int ColumnContrastCount(
        ComposerCapture capture,
        int x,
        byte background)
    {
        int count = 0;
        for (int y = capture.Height / 5; y < capture.Height * 19 / 20; y++)
        {
            if (Math.Abs(GetLuminance(capture, x, y) - background) >= 24)
            {
                count++;
            }
        }

        return count;
    }

    private static bool TryFindSendButton(
        ComposerCapture capture,
        byte background,
        int scrollbarLeft,
        out CircleCandidate best)
    {
        int scaleRadius = ComposerAnchorPlacement.Scale(1, capture.Dpi);
        int minimumRadius = Math.Max(12, 18 * scaleRadius);
        int maximumRadius = Math.Max(minimumRadius, 28 * scaleRadius);
        best = default;
        int rightLimit = Math.Max(minimumRadius, scrollbarLeft - minimumRadius - 8);
        var candidates = new List<ScoredCircleCandidate>();

        int sampleStep = Math.Max(4, scaleRadius * 3);
        for (int radius = minimumRadius;
             radius <= maximumRadius;
             radius += Math.Max(4, scaleRadius * 4))
        {
            for (int y = Math.Max(radius, capture.Height / 2);
                 y < capture.Height - radius;
                 y += sampleStep)
            {
                for (int x = Math.Max(radius, capture.Width * 2 / 5);
                     x < rightLimit;
                     x += sampleStep)
                {
                    double inner = SampleRing(capture, x, y, radius * 0.45);
                    double outer = SampleRing(capture, x, y, radius * 1.25);
                    double center = GetLuminance(capture, x, y);
                    double contrast = Math.Abs(((inner + center) / 2) - outer);
                    double backgroundMatch = Math.Abs(outer - background);
                    double score = contrast - (backgroundMatch * 0.35);
                    if (contrast >= 45)
                    {
                        candidates.Add(new ScoredCircleCandidate(
                            new CircleCandidate(x, y, radius, contrast),
                            score));
                    }
                }
            }
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        double strongestScore = candidates.Max(candidate => candidate.Score);
        var peaks = new List<ScoredCircleCandidate>();
        foreach (ScoredCircleCandidate candidate in candidates
                     .Where(candidate => candidate.Score >= strongestScore * 0.35)
                     .OrderByDescending(candidate => candidate.Score))
        {
            bool overlapsExistingPeak = peaks.Any(existing =>
            {
                int deltaX = candidate.Circle.CenterX - existing.Circle.CenterX;
                int deltaY = candidate.Circle.CenterY - existing.Circle.CenterY;
                double minimumSeparation = Math.Max(
                    candidate.Circle.Radius,
                    existing.Circle.Radius) * 1.5;
                return ((deltaX * deltaX) + (deltaY * deltaY))
                    <= minimumSeparation * minimumSeparation;
            });
            if (!overlapsExistingPeak)
            {
                peaks.Add(candidate);
            }
        }

        ScoredCircleCandidate selected = peaks
            .OrderByDescending(candidate => candidate.Circle.CenterX)
            .First();
        best = selected.Circle;
        return true;
    }

    private static double SampleRing(
        ComposerCapture capture,
        int centerX,
        int centerY,
        double radius)
    {
        double sum = 0;
        const int samples = 16;
        for (int index = 0; index < samples; index++)
        {
            double angle = index * Math.PI * 2 / samples;
            int x = Math.Clamp(
                centerX + (int)Math.Round(Math.Cos(angle) * radius),
                0,
                capture.Width - 1);
            int y = Math.Clamp(
                centerY + (int)Math.Round(Math.Sin(angle) * radius),
                0,
                capture.Height - 1);
            sum += GetLuminance(capture, x, y);
        }

        return sum / samples;
    }

    private static byte GetLuminance(ComposerCapture capture, int x, int y)
    {
        int index = (y * capture.Stride) + (x * 4);
        int blue = capture.BgraPixels[index];
        int green = capture.BgraPixels[index + 1];
        int red = capture.BgraPixels[index + 2];
        return (byte)((red * 54 + green * 183 + blue * 19) >> 8);
    }

    private readonly record struct CircleCandidate(
        int CenterX,
        int CenterY,
        int Radius,
        double Contrast);

    private readonly record struct ScoredCircleCandidate(
        CircleCandidate Circle,
        double Score);
}
