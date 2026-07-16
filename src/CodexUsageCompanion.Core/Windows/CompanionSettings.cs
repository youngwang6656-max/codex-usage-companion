using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexUsageCompanion.Windows;

public sealed record CompanionUserSettings(
    CompanionVisibilityPreference Visibility,
    FullscreenRevealPolicy FullscreenReveal)
{
    public static CompanionUserSettings Default { get; } = new(
        CompanionVisibilityPreference.Expanded,
        FullscreenRevealPolicy.DeferredUntilWindowed);
}

public interface ICompanionSettingsStore
{
    Task<CompanionUserSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(
        CompanionUserSettings settings,
        CancellationToken cancellationToken);
}

public sealed class JsonCompanionSettingsStore : ICompanionSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonCompanionSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public static JsonCompanionSettingsStore CreateDefault()
    {
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return new JsonCompanionSettingsStore(Path.Combine(
            localAppData,
            "CodexUsageCompanion",
            "settings.json"));
    }

    public async Task<CompanionUserSettings> LoadAsync(
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
            {
                return CompanionUserSettings.Default;
            }

            try
            {
                await using FileStream stream = new(
                    _path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    useAsync: true);
                CompanionUserSettings? settings = await JsonSerializer.DeserializeAsync<CompanionUserSettings>(
                    stream,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                if (settings is null)
                {
                    return CompanionUserSettings.Default;
                }

                return settings.FullscreenReveal == FullscreenRevealPolicy.DynamicIntegration
                    ? settings with
                    {
                        FullscreenReveal = FullscreenRevealPolicy.DeferredUntilWindowed,
                    }
                    : settings;
            }
            catch (JsonException)
            {
                return CompanionUserSettings.Default;
            }
            catch (IOException)
            {
                return CompanionUserSettings.Default;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        CompanionUserSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string temporaryPath = _path + ".tmp";
        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using (FileStream stream = new(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    settings,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            _gate.Release();
        }
    }
}
