namespace CodexUsageCompanion.Windows;

public static class CodexWindowSelector
{
    public static CodexWindowState? Select(
        IEnumerable<CodexWindowCandidate> candidates,
        nint foregroundWindow)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        List<CodexWindowCandidate> codexWindows = candidates
            .Where(IsCodexDesktopWindow)
            .ToList();

        CodexWindowCandidate? selected = codexWindows.FirstOrDefault(
                candidate => candidate.Handle == foregroundWindow && candidate.IsVisible)
            ?? codexWindows.FirstOrDefault(candidate => candidate.IsVisible && !candidate.IsMinimized)
            ?? codexWindows.FirstOrDefault(candidate => candidate.IsVisible)
            ?? codexWindows.FirstOrDefault();

        return selected is null
            ? null
            : new CodexWindowState(
                selected.Handle,
                selected.Bounds,
                selected.Dpi,
                selected.IsVisible && !selected.IsCloaked,
                selected.IsMinimized,
                selected.ExecutablePath!,
                selected.IsMaximized);
    }

    private static bool IsCodexDesktopWindow(CodexWindowCandidate candidate)
    {
        if (candidate.IsCloaked
            || candidate.IsToolWindow
            || candidate.Handle == 0
            || candidate.Bounds.Width <= 0
            || candidate.Bounds.Height <= 0
            || string.IsNullOrWhiteSpace(candidate.ExecutablePath))
        {
            return false;
        }

        return candidate.ExecutablePath.Contains("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                Path.GetFileName(candidate.ExecutablePath),
                "ChatGPT.exe",
                StringComparison.OrdinalIgnoreCase);
    }
}
