using CodexUsageCompanion.Windows;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class CompanionSettingsStoreTests
{
    [Fact]
    public async Task MissingFileUsesExpandedDeferredFullscreenDefaults()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            var store = new JsonCompanionSettingsStore(Path.Combine(directory, "settings.json"));

            CompanionUserSettings settings = await store.LoadAsync(
                TestContext.Current.CancellationToken);

            Assert.Equal(CompanionVisibilityPreference.Expanded, settings.Visibility);
            Assert.Equal(FullscreenRevealPolicy.DeferredUntilWindowed, settings.FullscreenReveal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LegacyDynamicFullscreenPreferenceMigratesToDeferredMode()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(
                path,
                """
                {
                  "visibility": "collapsed",
                  "fullscreenReveal": "dynamicIntegration"
                }
                """,
                TestContext.Current.CancellationToken);
            var store = new JsonCompanionSettingsStore(path);

            CompanionUserSettings settings = await store.LoadAsync(
                TestContext.Current.CancellationToken);

            Assert.Equal(CompanionVisibilityPreference.Collapsed, settings.Visibility);
            Assert.Equal(
                FullscreenRevealPolicy.DeferredUntilWindowed,
                settings.FullscreenReveal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveThenLoadRoundTripsOnlyVisibilityPreferences()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "nested", "settings.json");
            var store = new JsonCompanionSettingsStore(path);
            var expected = new CompanionUserSettings(
                CompanionVisibilityPreference.Collapsed,
                FullscreenRevealPolicy.DeferredUntilWindowed);

            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            await store.SaveAsync(expected, cancellationToken);
            CompanionUserSettings actual = await store.LoadAsync(cancellationToken);

            Assert.Equal(expected, actual);
            Assert.False(File.Exists(path + ".tmp"));
            string json = await File.ReadAllTextAsync(path, cancellationToken);
            Assert.DoesNotContain("account", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("anchor", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "CodexUsageCompanion.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
