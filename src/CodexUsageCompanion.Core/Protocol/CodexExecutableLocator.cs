using System.Diagnostics;

namespace CodexUsageCompanion.Protocol;

public static class CodexExecutableLocator
{
    public static string? Find()
    {
        IEnumerable<string> runningDesktopPaths = Process.GetProcessesByName("ChatGPT")
            .Select(TryGetExecutablePath)
            .Where(path => path is not null)
            .Cast<string>();
        IEnumerable<string> runningCliPaths = Process.GetProcessesByName("codex")
            .Select(TryGetExecutablePath)
            .Where(path => path is not null)
            .Cast<string>()
            .Concat(FindInstalledUserCliPaths());

        return SelectExisting(
            Environment.GetEnvironmentVariable("CODEX_CLI_PATH"),
            runningCliPaths,
            runningDesktopPaths,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            File.Exists);
    }

    public static string? SelectExisting(
        string? explicitOverride,
        IEnumerable<string> runningExecutablePaths,
        string userProfile,
        Func<string, bool> fileExists) =>
        SelectExisting(
            explicitOverride,
            [],
            runningExecutablePaths,
            userProfile,
            fileExists);

    public static string? SelectExisting(
        string? explicitOverride,
        IEnumerable<string> runningCodexExecutablePaths,
        IEnumerable<string> runningDesktopExecutablePaths,
        string userProfile,
        Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(runningCodexExecutablePaths);
        ArgumentNullException.ThrowIfNull(runningDesktopExecutablePaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(userProfile);
        ArgumentNullException.ThrowIfNull(fileExists);

        if (!string.IsNullOrWhiteSpace(explicitOverride) && fileExists(explicitOverride))
        {
            return explicitOverride;
        }

        string? userCli = runningCodexExecutablePaths
            .Where(path => !path.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase))
            .Where(path => string.Equals(Path.GetFileName(path), "codex.exe", StringComparison.OrdinalIgnoreCase))
            .Where(fileExists)
            .OrderByDescending(path => path.Contains("\\OpenAI\\Codex\\bin\\", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        if (userCli is not null)
        {
            return userCli;
        }

        string sandboxCli = Path.Combine(userProfile, ".codex", ".sandbox-bin", "codex.exe");
        return fileExists(sandboxCli) ? sandboxCli : null;
    }

    private static IEnumerable<string> FindInstalledUserCliPaths()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenAI",
            "Codex",
            "bin");
        if (!Directory.Exists(root))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(root, "codex.exe", SearchOption.AllDirectories).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? TryGetExecutablePath(Process process)
    {
        try
        {
            using (process)
            {
                return process.MainModule?.FileName;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            process.Dispose();
            return null;
        }
    }
}
