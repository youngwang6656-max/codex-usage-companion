using CodexUsageCompanion.Protocol;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class CodexExecutableLocatorTests
{
    [Fact]
    public void SelectExisting_PrefersExplicitOverride()
    {
        string result = CodexExecutableLocator.SelectExisting(
            explicitOverride: @"D:\tools\codex.exe",
            runningExecutablePaths: [@"C:\Program Files\WindowsApps\OpenAI.Codex_1\app\ChatGPT.exe"],
            userProfile: @"C:\Users\Test",
            fileExists: path => path is @"D:\tools\codex.exe" or @"C:\Program Files\WindowsApps\OpenAI.Codex_1\app\resources\codex.exe")!;

        Assert.Equal(@"D:\tools\codex.exe", result);
    }

    [Fact]
    public void SelectExisting_DoesNotSelectProtectedWindowsAppsCli()
    {
        string expected = @"C:\Program Files\WindowsApps\OpenAI.Codex_1\app\resources\codex.exe";

        string? result = CodexExecutableLocator.SelectExisting(
            explicitOverride: null,
            runningExecutablePaths: [@"C:\Program Files\WindowsApps\OpenAI.Codex_1\app\ChatGPT.exe"],
            userProfile: @"C:\Users\Test",
            fileExists: path => path == expected);

        Assert.Null(result);
    }

    [Fact]
    public void SelectExisting_DoesNotTreatOrdinaryChatGptAsCodex()
    {
        string? result = CodexExecutableLocator.SelectExisting(
            explicitOverride: null,
            runningExecutablePaths: [@"C:\Program Files\WindowsApps\OpenAI.ChatGPT_1\app\ChatGPT.exe"],
            userProfile: @"C:\Users\Test",
            fileExists: _ => true);

        Assert.Equal(@"C:\Users\Test\.codex\.sandbox-bin\codex.exe", result);
    }

    [Fact]
    public void SelectExisting_ReturnsNullWhenNoCandidateExists()
    {
        string? result = CodexExecutableLocator.SelectExisting(
            explicitOverride: null,
            runningExecutablePaths: [],
            userProfile: @"C:\Users\Test",
            fileExists: _ => false);

        Assert.Null(result);
    }

    [Fact]
    public void SelectExisting_PrefersRunningUserCodexCliOverProtectedWindowsAppsCopy()
    {
        string expected = @"C:\Users\Test\AppData\Local\OpenAI\Codex\bin\build123\codex.exe";

        string? result = CodexExecutableLocator.SelectExisting(
            explicitOverride: null,
            runningCodexExecutablePaths: [expected],
            runningDesktopExecutablePaths: [@"C:\Program Files\WindowsApps\OpenAI.Codex_1\app\ChatGPT.exe"],
            userProfile: @"C:\Users\Test",
            fileExists: _ => true);

        Assert.Equal(expected, result);
    }
}
