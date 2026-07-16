using System.Diagnostics;
using CodexUsageCompanion.Protocol;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class StdioCodexAppServerSessionTests
{
    [Fact]
    public void BuildStartInfo_UsesHiddenRedirectedStdioTransport()
    {
        ProcessStartInfo startInfo = StdioCodexAppServerSession.BuildStartInfo(@"C:\Codex\codex.exe");

        Assert.Equal(@"C:\Codex\codex.exe", startInfo.FileName);
        Assert.Equal("app-server --listen stdio://", startInfo.Arguments);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.True(startInfo.RedirectStandardInput);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
    }
}
