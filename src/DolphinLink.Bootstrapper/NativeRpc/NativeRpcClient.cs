using System.Collections.Concurrent;
using DolphinLink.Client.Abstractions;
using DolphinLink.Bootstrapper.NativeRpc.Proto;
using File = DolphinLink.Bootstrapper.NativeRpc.Proto.File;

namespace DolphinLink.Bootstrapper.NativeRpc;

/// <summary>
/// High-level client for the Flipper Zero native protobuf RPC (CDC interface 0).
///
/// Provides the minimum set of operations required for bootstrapping: ping,
/// storage stat, md5sum, mkdir, chunked file write, and app launch.
///
/// The native RPC uses a simple request/response model: every <see cref="Main"/>
/// message has a <c>command_id</c> field; the device echoes the same id back in
/// its response so we can correlate them.  Multi-part responses chain frames with
/// <c>has_next = true</c>; the final frame has <c>has_next = false</c>.
///
/// Threading: a single dedicated reader loop dispatches all inbound messages.
/// </summary>
internal sealed class NativeRpcClient : IAsyncDisposable
{
    // Write chunk size matching qFlipper's default (512 bytes of binary data per frame).
    internal const int WriteChunkSize = 512;

    private readonly NativeRpcTransport _transport;
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<Main>> _pending = new();
    private Task? _readerTask;
    private readonly CancellationTokenSource _cts = new();
    // Wraps to 0 at uint.MaxValue (~4.3 billion); safe because the Flipper native RPC
    // is only used during bootstrapping (short-lived) and responses are correlated
    // before any collision can occur.
    private uint _nextId;
    private int _disposed; // 0 = alive, 1 = disposed (Interlocked)
    private int _opened;   // 0 = not yet, 1 = opened (Interlocked)

    /// <summary>
    /// Creates a client using the supplied <paramref name="port"/>.
    /// The port must not yet be open; <see cref="OpenAsync"/> will open it
    /// as part of the CLI-to-protobuf handshake.
    /// </summary>
    internal NativeRpcClient(ISerialPort port)
    {
        _transport = new NativeRpcTransport(port);
    }

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    /// <summary>Opens the serial port and starts the background reader loop.</summary>
    internal async ValueTask OpenAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _opened, 1) == 1)
        {
            throw new InvalidOperationException("OpenAsync has already been called.");
        }

        // OpenAsync handles stabilization delay and raw-byte drain of any
        // session-start noise the Flipper emits on connect.
        await _transport.OpenAsync(ct).ConfigureAwait(false);
        _readerTask = Task.Run(() => ReaderLoopAsync(_cts.Token));
    }

    private async Task ReaderLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Main msg;
            try
            {
                msg = await _transport.ReceiveAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Transport error (e.g. EndOfStreamException, IOException from
                // a device-level abort).  Fail all pending requests with the
                // original exception so callers get a meaningful message.
                FailAll(new IOException("Native RPC transport error.", ex));
                break;
            }
            catch
            {
                // ct was cancelled while the read was in-flight — exit cleanly.
                break;
            }

            if (_pending.TryGetValue(msg.CommandId, out var tcs))
            {
                // Only complete the TCS on the final frame (has_next = false).
                if (!msg.HasNext)
                {
                    _pending.TryRemove(msg.CommandId, out _);
                    tcs.TrySetResult(msg);
                }
            }
        }
    }

    /// <summary>
    /// Faults all pending requests.  Uses TryRemove per key so that entries
    /// added concurrently between iteration and clear are not silently dropped.
    /// </summary>
    private void FailAll(Exception ex)
    {
        foreach (var key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out var tcs))
            {
                tcs.TrySetException(ex);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Core send/receive
    // -------------------------------------------------------------------------

    private uint NextId() => Interlocked.Increment(ref _nextId);

    /// <summary>
    /// Sends <paramref name="request"/> and awaits the corresponding response frame.
    /// </summary>
    private async Task<Main> SendAsync(Main request, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

        var tcs = new TaskCompletionSource<Main>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Register before sending to avoid the reader-loop race.
        _pending[request.CommandId] = tcs;

        using var reg = ct.Register(() =>
        {
            _pending.TryRemove(request.CommandId, out _);
            tcs.TrySetCanceled(ct);
        });

        await _transport.SendAsync(request, ct).ConfigureAwait(false);
        Main response = await tcs.Task.ConfigureAwait(false);

        if (response.CommandStatus != CommandStatus.Ok)
        {
            throw new NativeRpcException(response.CommandStatus,
                $"Native RPC error: {response.CommandStatus} (command_id={request.CommandId})");
        }

        return response;
    }

    // -------------------------------------------------------------------------
    // Public API surface (internal to the bootstrapper assembly)
    // -------------------------------------------------------------------------

    /// <summary>Sends a system ping to verify the connection.</summary>
    internal async Task PingAsync(CancellationToken ct = default)
    {
        var req = new Main
        {
            CommandId         = NextId(),
            SystemPingRequest = new PingRequest { Data = ByteString.Empty },
        };
        await SendAsync(req, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the stat entry for <paramref name="path"/>, or <c>null</c> if the path
    /// does not exist (<see cref="CommandStatus.ErrorStorageNotExist"/>).
    /// </summary>
    internal async Task<File?> StorageStatAsync(string path, CancellationToken ct = default)
    {
        var req = new Main
        {
            CommandId          = NextId(),
            StorageStatRequest = new StatRequest { Path = path },
        };
        try
        {
            Main resp = await SendAsync(req, ct).ConfigureAwait(false);
            return resp.StorageStatResponse?.File;
        }
        catch (NativeRpcException ex) when (ex.Status == CommandStatus.ErrorStorageNotExist)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the MD5 hex string for the file at <paramref name="path"/>,
    /// or <c>null</c> if the path does not exist.
    /// </summary>
    internal async Task<string?> StorageMd5SumAsync(string path, CancellationToken ct = default)
    {
        var req = new Main
        {
            CommandId            = NextId(),
            StorageMd5SumRequest = new Md5sumRequest { Path = path },
        };
        try
        {
            Main resp = await SendAsync(req, ct).ConfigureAwait(false);
            return resp.StorageMd5SumResponse?.Md5Sum;
        }
        catch (NativeRpcException ex) when (ex.Status == CommandStatus.ErrorStorageNotExist)
        {
            return null;
        }
    }

    /// <summary>
    /// Creates the directory at <paramref name="path"/>. Silently succeeds if it
    /// already exists.
    /// </summary>
    internal async Task StorageMkdirAsync(string path, CancellationToken ct = default)
    {
        var req = new Main
        {
            CommandId           = NextId(),
            StorageMkdirRequest = new MkdirRequest { Path = path },
        };
        try
        {
            await SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (NativeRpcException ex) when (ex.Status == CommandStatus.ErrorStorageExist)
        {
            // Directory already exists — acceptable.
        }
    }

    /// <summary>
    /// Uploads <paramref name="data"/> to <paramref name="path"/> on the Flipper SD card,
    /// overwriting any existing file.  Splits large payloads into
    /// <see cref="WriteChunkSize"/>-byte chunks using the native RPC's
    /// <c>has_next</c> continuation mechanism, matching qFlipper's behaviour.
    ///
    /// All chunks in a multi-frame write share the same <c>command_id</c> so the
    /// Flipper can correlate them correctly.  Only the final frame (has_next=false)
    /// elicits a response; intermediate frames are sent fire-and-forget.
    /// </summary>
    internal async Task StorageWriteAsync(
        string path,
        byte[] data,
        IProgress<(int Written, int Total)>? progress = null,
        CancellationToken ct = default)
    {
        int total   = data.Length;
        int written = 0;

        // All chunks share a single command_id so the Flipper correlates them.
        uint writeId = NextId();

        while (written < total || total == 0)
        {
            int chunkSize = Math.Min(WriteChunkSize, total - written);
            bool isLast   = (written + chunkSize) >= total;

            var chunk = ByteString.CopyFrom(data, written, chunkSize);

            var req = new Main
            {
                CommandId           = writeId,
                HasNext             = !isLast,
                StorageWriteRequest = new WriteRequest
                {
                    Path = path,
                    File = new File { Data = chunk },
                },
            };

            // Only the final frame (has_next=false) receives a device response.
            if (isLast)
            {
                await SendAsync(req, ct).ConfigureAwait(false);
            }
            else
            {
                // Intermediate frames: fire-and-forget; device buffers them silently.
                await _transport.SendAsync(req, ct).ConfigureAwait(false);
            }

            written += chunkSize;
            progress?.Report((written, total));

            // Handle empty-file edge case: exit after a single empty write.
            if (total == 0)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Launches the application at <paramref name="path"/> on the Flipper
    /// via the native <c>app_start_request</c>.
    /// </summary>
    internal async Task AppStartAsync(string path, CancellationToken ct = default)
    {
        var req = new Main
        {
            CommandId       = NextId(),
            AppStartRequest = new StartRequest { Name = path },
        };
        await SendAsync(req, ct).ConfigureAwait(false);
    }

    // -------------------------------------------------------------------------
    // Dispose
    // -------------------------------------------------------------------------

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);
        FailAll(new ObjectDisposedException(nameof(NativeRpcClient)));

        // Dispose the transport BEFORE awaiting the reader task.
        // The reader task is blocked on ReceiveAsync (a ReadAsync on the serial
        // stream).  On Windows, ReadAsync ignores CancellationToken; the only
        // way to unblock it is to close the underlying OS handle, which
        // NativeRpcTransport.DisposeAsync does via reflection.  Awaiting
        // _readerTask first would deadlock for the same reason as the identical
        // pattern fixed in RpcClient / PacketSerializationTransport.
        await _transport.DisposeAsync().ConfigureAwait(false);

        if (_readerTask is not null)
        {
            try { await _readerTask.ConfigureAwait(false); }
            catch { /* best-effort */ }
        }

        // Dispose CTS last — the reader task checks ct.IsCancellationRequested
        // in its loop condition and we must not invalidate the token while the
        // task may still be running.
        _cts.Dispose();
    }
}

/// <summary>
/// Thrown when the Flipper native RPC returns a non-OK <see cref="CommandStatus"/>.
/// </summary>
public sealed class NativeRpcException : Exception
{
    /// <summary>The error status returned by the device.</summary>
    public CommandStatus Status { get; }

    internal NativeRpcException(CommandStatus status, string message)
        : base(message)
    {
        Status = status;
    }
}
