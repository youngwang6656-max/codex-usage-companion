using System.Diagnostics;
using System.Text;

namespace CodexUsageCompanion.Protocol;

public sealed class StdioCodexAppServerSession : ICodexAppServerSession
{
    private readonly string _codexExecutablePath;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private Process? _process;
    private CodexAppServerProtocolSession? _protocol;
    private bool _disposed;

    public StdioCodexAppServerSession(string codexExecutablePath)
    {
        _codexExecutablePath = !string.IsNullOrWhiteSpace(codexExecutablePath)
            ? codexExecutablePath
            : throw new ArgumentException("A Codex executable path is required.", nameof(codexExecutablePath));
    }

    public event EventHandler? RateLimitsUpdated;

    public event EventHandler<string?>? AccountUpdated;

    public static ProcessStartInfo BuildStartInfo(string codexExecutablePath) => new()
    {
        FileName = codexExecutablePath,
        Arguments = "app-server --listen stdio://",
        UseShellExecute = false,
        CreateNoWindow = true,
        WindowStyle = ProcessWindowStyle.Hidden,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8,
    };

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning())
            {
                return;
            }

            await DisposeRuntimeAsync().ConfigureAwait(false);

            Process process = Process.Start(BuildStartInfo(_codexExecutablePath))
                ?? throw new CodexAppServerException("Unable to start Codex app-server.");
            _process = process;
            _ = DrainStandardErrorAsync(process);

            var connection = new JsonRpcLineConnection(process.StandardOutput, process.StandardInput);
            var protocol = new CodexAppServerProtocolSession(connection);
            protocol.RateLimitsUpdated += OnRateLimitsUpdated;
            protocol.AccountUpdated += OnAccountUpdated;
            _protocol = protocol;

            try
            {
                await protocol.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await DisposeRuntimeAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task<string> ReadAccountAsync(CancellationToken cancellationToken)
    {
        await StartAsync(cancellationToken).ConfigureAwait(false);
        return await (_protocol ?? throw new CodexAppServerException("Codex app-server is unavailable."))
            .ReadAccountAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string> ReadRateLimitsAsync(CancellationToken cancellationToken)
    {
        await StartAsync(cancellationToken).ConfigureAwait(false);
        return await (_protocol ?? throw new CodexAppServerException("Codex app-server is unavailable."))
            .ReadRateLimitsAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisposeRuntimeAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
            _lifecycleLock.Dispose();
        }
    }

    private bool IsRunning()
    {
        try
        {
            return _process is { HasExited: false } && _protocol is not null;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private async Task DisposeRuntimeAsync()
    {
        if (_protocol is not null)
        {
            _protocol.RateLimitsUpdated -= OnRateLimitsUpdated;
            _protocol.AccountUpdated -= OnAccountUpdated;
            await _protocol.DisposeAsync().ConfigureAwait(false);
            _protocol = null;
        }

        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync().ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }
    }

    private static async Task DrainStandardErrorAsync(Process process)
    {
        try
        {
            while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is not null)
            {
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException or IOException)
        {
        }
    }

    private void OnRateLimitsUpdated(object? sender, EventArgs eventArgs) =>
        RateLimitsUpdated?.Invoke(this, EventArgs.Empty);

    private void OnAccountUpdated(object? sender, string? planType) =>
        AccountUpdated?.Invoke(this, planType);
}
