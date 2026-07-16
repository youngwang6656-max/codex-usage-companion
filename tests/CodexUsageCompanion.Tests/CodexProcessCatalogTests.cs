using CodexUsageCompanion.Windows;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class CodexProcessCatalogTests
{
    [Fact]
    public void GetKnownProcesses_CachesResolvedCodexPathsUntilInvalidated()
    {
        var provider = new FakeProcessProvider
        {
            Snapshots =
            [
                new(101, @"C:\Program Files\WindowsApps\OpenAI.Codex_1\app\ChatGPT.exe"),
                new(102, @"C:\Program Files\WindowsApps\OpenAI.ChatGPT_1\app\ChatGPT.exe"),
            ],
        };
        var catalog = new CodexProcessCatalog(provider);

        IReadOnlyDictionary<uint, string> first = catalog.GetKnownProcesses();
        IReadOnlyDictionary<uint, string> second = catalog.GetKnownProcesses();

        Assert.Equal(1, provider.Reads);
        Assert.Single(first);
        Assert.Equal(first, second);
        Assert.Equal(@"C:\Program Files\WindowsApps\OpenAI.Codex_1\app\ChatGPT.exe", first[101]);
        Assert.False(first.ContainsKey(102));
    }

    [Fact]
    public void Invalidate_ForcesOneFreshProcessResolution()
    {
        var provider = new FakeProcessProvider
        {
            Snapshots = [new(101, @"C:\Program Files\WindowsApps\OpenAI.Codex_1\app\ChatGPT.exe")],
        };
        var catalog = new CodexProcessCatalog(provider);
        _ = catalog.GetKnownProcesses();
        catalog.Invalidate();
        provider.Snapshots = [new(202, @"C:\Program Files\WindowsApps\OpenAI.Codex_2\app\ChatGPT.exe")];

        IReadOnlyDictionary<uint, string> refreshed = catalog.GetKnownProcesses();

        Assert.Equal(2, provider.Reads);
        Assert.False(refreshed.ContainsKey(101));
        Assert.True(refreshed.ContainsKey(202));
    }

    private sealed class FakeProcessProvider : IProcessSnapshotProvider
    {
        public int Reads { get; private set; }

        public IReadOnlyList<ProcessSnapshot> Snapshots { get; set; } = [];

        public IReadOnlyList<ProcessSnapshot> GetChatGptProcesses()
        {
            Reads++;
            return Snapshots;
        }
    }
}
