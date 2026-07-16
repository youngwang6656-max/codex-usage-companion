using System.Collections.Concurrent;
using System.Text.Json;

namespace CodexUsageCompanion.Protocol;

public sealed class CodexAppServerException : Exception
{
    public CodexAppServerException(string message)
        : base(message)
    {
    }
}

public sealed class JsonRpcLineConnection : IJsonRpcConnection
{
    private readonly TextReader _reader;
    private readonly TextWriter _writer;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<string>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _readTask;
    private int _nextId;
    private bool _disposed;

    public JsonRpcLineConnection(TextReader reader, TextWriter writer)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public event EventHandler<JsonRpcNotification>? NotificationReceived;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _readTask ??= ReadLoopAsync(_lifetime.Token);
    }

    public async Task<string> RequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Start();

        int id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException("Could not register a Codex app-server request.");
        }

        using CancellationTokenRegistration registration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));

        try
        {
            await WriteLineAsync(JsonRpcCodec.CreateRequest(id, method, parameters), cancellationToken)
                .ConfigureAwait(false);
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public Task NotifyAsync(string method, object? parameters, CancellationToken cancellationToken) =>
        WriteLineAsync(JsonRpcCodec.CreateNotification(method, parameters), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();

        if (_readTask is not null)
        {
            try
            {
                await _readTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        FailPending(new CodexAppServerException("Codex app-server connection closed."));
        _reader.Dispose();
        _writer.Dispose();
        _writeLock.Dispose();
        _lifetime.Dispose();
    }

    private async Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _writer.WriteLineAsync(line).ConfigureAwait(false);
            await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                JsonRpcMessage message;
                try
                {
                    message = JsonRpcCodec.Parse(line);
                }
                catch (ProtocolException)
                {
                    continue;
                }

                if (message.Kind == JsonRpcMessageKind.Notification && message.Method is not null)
                {
                    NotificationReceived?.Invoke(
                        this,
                        new JsonRpcNotification(message.Method, message.Params));
                    continue;
                }

                if (message.Id is not int id || !_pending.TryRemove(id, out TaskCompletionSource<string>? completion))
                {
                    continue;
                }

                if (message.Kind == JsonRpcMessageKind.Error)
                {
                    completion.TrySetException(
                        new CodexAppServerException(message.ErrorMessage ?? "Codex app-server request failed."));
                }
                else if (message.Result is JsonElement result)
                {
                    completion.TrySetResult(result.GetRawText());
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            FailPending(new CodexAppServerException($"Codex app-server connection failed: {exception.Message}"));
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                FailPending(new CodexAppServerException("Codex app-server connection closed."));
            }
        }
    }

    private void FailPending(Exception exception)
    {
        foreach ((int id, TaskCompletionSource<string> completion) in _pending)
        {
            if (_pending.TryRemove(id, out _))
            {
                completion.TrySetException(exception);
            }
        }
    }
}
