using System.Diagnostics;

namespace CodexUsageCompanion.Windows;

public sealed record ProcessSnapshot(uint ProcessId, string? ExecutablePath);

public interface IProcessSnapshotProvider
{
    IReadOnlyList<ProcessSnapshot> GetChatGptProcesses();
}

public sealed class SystemProcessSnapshotProvider : IProcessSnapshotProvider
{
    public IReadOnlyList<ProcessSnapshot> GetChatGptProcesses()
    {
        var snapshots = new List<ProcessSnapshot>();
        foreach (Process process in Process.GetProcessesByName("ChatGPT"))
        {
            using (process)
            {
                string? path;
                try
                {
                    path = process.MainModule?.FileName;
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    path = null;
                }

                snapshots.Add(new ProcessSnapshot(checked((uint)process.Id), path));
            }
        }

        return snapshots;
    }
}

public sealed class CodexProcessCatalog
{
    private readonly IProcessSnapshotProvider _provider;
    private readonly object _gate = new();
    private IReadOnlyDictionary<uint, string>? _cached;

    public CodexProcessCatalog(IProcessSnapshotProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public IReadOnlyDictionary<uint, string> GetKnownProcesses()
    {
        lock (_gate)
        {
            if (_cached is not null)
            {
                return _cached;
            }

            _cached = _provider.GetChatGptProcesses()
                .Where(snapshot => snapshot.ExecutablePath?.Contains(
                    "OpenAI.Codex_",
                    StringComparison.OrdinalIgnoreCase) == true)
                .Where(snapshot => string.Equals(
                    Path.GetFileName(snapshot.ExecutablePath),
                    "ChatGPT.exe",
                    StringComparison.OrdinalIgnoreCase))
                .GroupBy(snapshot => snapshot.ProcessId)
                .ToDictionary(group => group.Key, group => group.First().ExecutablePath!);
            return _cached;
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _cached = null;
        }
    }
}
