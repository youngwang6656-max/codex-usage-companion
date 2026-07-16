using CodexUsageCompanion.Windows;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class CodexWindowSelectorTests
{
    [Fact]
    public void Select_PrefersForegroundCodexWindow()
    {
        CodexWindowCandidate first = Candidate((nint)1, @"C:\Program Files\WindowsApps\OpenAI.Codex_1\app\ChatGPT.exe");
        CodexWindowCandidate second = Candidate((nint)2, @"C:\Program Files\WindowsApps\OpenAI.Codex_1\app\ChatGPT.exe");

        CodexWindowState? result = CodexWindowSelector.Select([first, second], foregroundWindow: (nint)2);

        Assert.Equal((nint)2, result!.Handle);
    }

    [Fact]
    public void Select_RejectsOrdinaryChatGptAndNonWindowProcesses()
    {
        CodexWindowCandidate chatGpt = Candidate(
            (nint)1,
            @"C:\Program Files\WindowsApps\OpenAI.ChatGPT_1\app\ChatGPT.exe");
        CodexWindowCandidate cli = Candidate(
            (nint)2,
            @"C:\Program Files\WindowsApps\OpenAI.Codex_1\app\resources\codex.exe");

        CodexWindowState? result = CodexWindowSelector.Select([chatGpt, cli], foregroundWindow: (nint)1);

        Assert.Null(result);
    }

    [Fact]
    public void Select_KeepsMinimizedCodexSoTheCompanionCanSynchronizeHiddenState()
    {
        CodexWindowCandidate candidate = Candidate(
            (nint)7,
            @"C:\Program Files\WindowsApps\OpenAI.Codex_1\app\ChatGPT.exe") with
        {
            IsVisible = true,
            IsMinimized = true,
        };

        CodexWindowState? result = CodexWindowSelector.Select([candidate], foregroundWindow: 0);

        Assert.NotNull(result);
        Assert.True(result.IsMinimized);
    }

    [Fact]
    public void Select_FallsBackToFirstVisibleCodexWhenForegroundIsAnotherApp()
    {
        CodexWindowCandidate hidden = Candidate(
            (nint)1,
            @"C:\Program Files\WindowsApps\OpenAI.Codex_1\app\ChatGPT.exe") with
        { IsVisible = false };
        CodexWindowCandidate visible = Candidate(
            (nint)2,
            @"C:\Program Files\WindowsApps\OpenAI.Codex_1\app\ChatGPT.exe");

        CodexWindowState? result = CodexWindowSelector.Select([hidden, visible], foregroundWindow: (nint)99);

        Assert.Equal((nint)2, result!.Handle);
    }

    [Fact]
    public void Select_PreservesMaximizedState()
    {
        CodexWindowCandidate candidate = Candidate(
            (nint)8,
            @"C:\Program Files\WindowsApps\OpenAI.Codex_1\app\ChatGPT.exe") with
        { IsMaximized = true };

        CodexWindowState? result = CodexWindowSelector.Select([candidate], foregroundWindow: (nint)8);

        Assert.True(result!.IsMaximized);
    }

    [Fact]
    public void Select_RejectsVisibleCodexToolWindowsThatReuseChatGptTitle()
    {
        CodexWindowCandidate toolWindow = Candidate(
            (nint)9,
            @"C:\Program Files\WindowsApps\OpenAI.Codex_1\app\ChatGPT.exe") with
        { IsToolWindow = true };
        CodexWindowCandidate mainWindow = Candidate(
            (nint)10,
            @"C:\Program Files\WindowsApps\OpenAI.Codex_1\app\ChatGPT.exe");

        CodexWindowState? result = CodexWindowSelector.Select(
            [toolWindow, mainWindow],
            foregroundWindow: (nint)9);

        Assert.Equal((nint)10, result!.Handle);
    }

    private static CodexWindowCandidate Candidate(nint handle, string path) => new(
        handle,
        path,
        "Codex",
        new PixelRect(100, 100, 1000, 700),
        Dpi: 96,
        IsVisible: true,
        IsMinimized: false,
        IsCloaked: false);
}
