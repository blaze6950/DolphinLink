# FlipperZero.NET — Architecture

---

## C Daemon Threading Model

The daemon runs entirely on the **Flipper Zero's main application thread**, driven by a `FuriEventLoop`. A dedicated TX thread handles USB output. Hardware callbacks from subsystem workers post events into queues that the event loop drains.

```
USB ISR  cdc_rx_callback()
  │  furi_hal_cdc_receive() → byte accumulation → on '\n':
  └─► furi_message_queue_put(rx_queue, ...)       [ISR-safe, non-blocking]

USB ISR  cdc_tx_callback()
  └─► furi_semaphore_release(tx_semaphore)        [paces TX thread]

FuriEventLoop (main thread)
  ├── on_rx_queue        → rpc_dispatch() → handler → cdc_send()
  ├── on_stream_event    → build {"t":1,...}\n → cdc_send()
  ├── on_ctrl_line_queue → DTR connect/disconnect handling
  └── on_input_queue     → UI input events

TX thread "RpcTx"  (512-byte stack)
  └── drains tx_stream (FuriStreamBuffer, 512 bytes)
      in ≤64-byte chunks, paced by tx_semaphore
      sends ZLP when last chunk was exactly 64 bytes

Subsystem worker threads / ISRs
  └─► furi_message_queue_put(stream_event_queue, ...) → on_stream_event
```

### Queue sizes

| Queue                | Depth | Element type                  |
|----------------------|-------|-------------------------------|
| `rx_queue`           | 16    | `RxLine` (1024 + 2 bytes)     |
| `stream_event_queue` | 32    | `StreamEvent` (128 + 4 bytes) |
| `cdc_ctrl_queue`     | 2     | `CdcCtrlEvent` (1 byte)       |
| `input_queue`        | 8     | `InputEvent`                  |

### ISR constraints

Only `furi_hal_cdc_receive()` and `furi_message_queue_put()` are safe in ISR context. Never call `snprintf`, `FURI_LOG_*`, `furi_hal_cdc_send`, or any blocking function from an ISR or USB callback. GPIO EXTI stream callbacks pre-compose JSON fragments (`gpio_frag_high`/`gpio_frag_low`) at stream-open time so no formatting is needed in the ISR.

### USB configuration

The daemon uses `usb_cdc_dual` and CDC interface 1 (`RPC_CDC_IF 1`). Interface 0 remains available for the Flipper's native qFlipper/protobuf RPC. On startup the daemon saves the previous USB config with `furi_hal_usb_get_config()` and restores it on exit. CDC callbacks are cleared before restoring.

### Startup and teardown

**Startup:**
1. Allocate message queues.
2. Open `Storage` and `NotificationApp` from Furi records.
3. Init GUI (`ViewPort`), transport (`cdc_transport_init()`), stream table, resource bitmask.
4. Set `usb_cdc_dual`; retry up to 20 × 100 ms if USB is locked.
5. Register `CdcCallbacks` on interface 1.
6. Subscribe all four queues on the event loop.
7. Start the event loop (`furi_event_loop_run()`).

**Teardown (on `daemon_stop` or FAP exit):**
1. Send `{"t":2}\n` (graceful exit signal to host) while TX pipeline is still running.
2. `stream_close_all()` → `resource_reset()`.
3. Unsubscribe queues; free heartbeat timer; free event loop.
4. `rpc_gui_teardown()`.
5. `furi_hal_cdc_set_callbacks(RPC_CDC_IF, NULL, NULL)`.
6. `cdc_transport_free()` (joins TX thread, frees stream buffer and semaphore).
7. `furi_hal_usb_set_config(prev_usb, NULL)`.
8. Free queues; close Furi records.

---

## C# Transport Stack

The C# client layers three transports on top of the raw serial port:

```
SerialPortTransport          (raw System.IO.Ports.SerialPort)
        ↑
PacketSerializationTransport (single-writer serialisation via BoundedChannel<string>(32))
        ↑
HeartbeatTransport           (bidirectional keep-alive + RX timeout)
        ↑
FlipperRpcClient             (RPC request/response and stream logic)
```

Each layer implements `IFlipperTransport`:

```csharp
public interface IFlipperTransport : IAsyncDisposable
{
    ValueTask OpenAsync(CancellationToken ct = default);
    ValueTask SendAsync(string data, CancellationToken ct = default);
    IAsyncEnumerable<string> ReceiveAsync(CancellationToken ct = default);
}
```

### SerialPortTransport

- Wraps `System.IO.Ports.SerialPort` with `DtrEnable=true`, `WriteTimeout=2000 ms`, `ReadTimeout=Infinite`.
- `SendAsync` calls `WriteLineAsync` + `FlushAsync` on the underlying `StreamWriter`.
- `ReceiveAsync` loops on `ReadLineAsync` from a `StreamReader`.
- On Windows, `DisposeAsync` force-closes the OS file handle via reflection to unblock pending `ReadLineAsync` (avoids a deadlock on serial port close).

### PacketSerializationTransport

- Serialises concurrent `SendAsync` calls through a `BoundedChannel<string>(32)` so only one caller writes to the inner transport at a time.
- A background `WriterLoopAsync` task drains the channel and calls `_inner.SendAsync`.
- `ReceiveAsync` forwards directly to the inner transport.
- `DisposeAsync` order: seal channel → cancel writer CTS → dispose inner transport → await writer task. The inner dispose comes first to unblock any pending `WriteLineAsync` on Windows.

### HeartbeatTransport

- Sends a keep-alive (`string.Empty` → bare `\n` on wire) when TX has been idle for `heartbeatInterval` (default 3 s).
- Tracks last-sent and last-seen timestamps using `Interlocked.Exchange` on `long` tick values.
- Fires `event Action? Disconnected` exactly once (guarded by `Interlocked.CompareExchange`) when the RX timeout expires or a send fails.
- `ReceiveAsync` updates `_lastSeenTicks` on every line including keep-alives, then `continue`s on whitespace-only lines so they are invisible to the layer above.
- The constraint `timeout > heartbeatInterval` is enforced in the constructor.

---

## C# Client Internal Flow

### Connection

```
ConnectAsync()
  1. _transport.OpenAsync()
  2. Start background ReaderLoopAsync()
  3. Send daemon_info → verify name + version
  4. If configure is advertised: send configure (heartbeat timing + LED colour)
```

### Sending a request

```
SendAsync<TCommand, TResponse>()
  1. Atomically increment _nextId
  2. pending.Register(id)         ← BEFORE sending (prevents reader-loop race)
  3. Serialize: {"c":cmdId,"i":id,...args...}
  4. _transport.SendAsync(json)
  5. StampSentTicks(id)
  6. await pending.Task
```

### Reader loop

```
ReaderLoopAsync()  [background task]
  await foreach line in _transport.ReceiveAsync()
    envelope = RpcEnvelope.Parse(line)
    if envelope.Type == Disconnect → FaultAll(DaemonExited); return
    else                           → dispatcher.Dispatch(envelope)
  // EOF (non-cancelled) → FaultAll(ConnectionLost)
```

`RpcMessageDispatcher.Dispatch`:
- `Response` → route to `RpcPendingRequests` by `envelope.Id`
- `Event` → route to `RpcStreamManager` by `envelope.Id` (stream ID)

### Opening a stream

```
SendStreamAsync<TCommand, TEvent>()
  1. Send command, await StreamOpenResult (contains StreamId)
  2. _streams.CreateStream<TEvent>(streamId, disconnectToken)
  3. Wire Closed callback → CloseStreamAsync (sends stream_close)
  4. Return RpcStream<TEvent>
```

`RpcStream<TEvent>` is an `IAsyncEnumerable<TEvent>` backed by a `ChannelReader<JsonElement>`. Events are deserialized lazily during iteration. On disconnect, the stream surfaces `FlipperDisconnectedException` rather than `OperationCanceledException`.

### Disconnect and fault propagation

`FaultAll(FlipperDisconnectedException)` is idempotent (Interlocked guard):
1. Cancel `_cts` → stops reader loop.
2. `_pending.FailAll(ex)` → fails all in-flight `SendAsync` tasks.
3. `_streams.FaultAll(ex)` → faults all open stream channels.
4. Cancel `_disconnectCts` → signals the `Disconnected` token.

`DisposeAsync` order: `FaultAll(ClientDisposed)` → `_transport.DisposeAsync()` (unblocks Windows `ReadLineAsync`) → `await _readerTask`.

### Key internal types

| Type                        | Role                                                                                     |
|-----------------------------|------------------------------------------------------------------------------------------|
| `RpcPendingRequests`        | `ConcurrentDictionary<uint, IPendingRequest>` for in-flight requests                     |
| `PendingRequest<TResponse>` | `TaskCompletionSource<TResponse>` with `RunContinuationsAsynchronously`                  |
| `RpcStreamManager`          | `ConcurrentDictionary<uint, StreamState>` for open streams                               |
| `StreamState`               | Unbounded `Channel<JsonElement>` (`SingleReader`, `SingleWriter`)                        |
| `RpcEnvelope`               | Parsed inbound envelope; `RpcMessageType`: `Response=0`, `Event=1`, `Disconnect=2`       |
| `RpcMessageSerializer`      | Serialises outbound commands into `ArrayBufferWriter<byte>` (no intermediate allocation) |

### JSON Normalizer

`RpcJsonNormalizer` (namespace `FlipperZero.NET`) transforms compact wire-format JSON into
human-readable JSON for diagnostics and logging. It expands abbreviated wire keys to full names,
resolves integer enum values to named constants, converts numeric booleans (1/0) to true/false,
and maps command IDs to command names.

```csharp
// Envelope-only expansion (no payload key expansion):
var readable = RpcJsonNormalizer.Normalize(entry.RawJson);

// Full expansion including payload keys:
var readable = RpcJsonNormalizer.Normalize(entry.RawJson, "gpio_read");
```

The class is split across two files:

- `Generated/RpcJsonNormalizer.g.cs` — generated by `codegens/normalizer-codegen.csx`; contains
  all lookup switch expressions (`CommandIdToName`, `ExpandRequestKey`, `ExpandResponseKey`,
  `IsRequestBoolField`, `IsResponseBoolField`, `ExpandRequestEnum`, `ExpandResponseEnum`).
- `RpcJsonNormalizer.cs` — hand-written; contains the `Normalize()` and `NormalizeCore()` methods
  and the `NormToken` struct.

All lookup tables are switch expressions for zero-allocation, JIT-optimized dispatch. Output is
produced via `string.Create` for single-allocation rendering.

---

## Bootstrapper Flow

`FlipperBootstrapper.BootstrapAsync` connects a physical Flipper Zero and ensures the daemon FAP is installed and running.

```
BootstrapAsync(systemPort, daemonPort)
  1. Fast path: TryConnectDaemonDirectAsync (5 s timeout)
     └─ success → return AlreadyRunning (daemon already running)

  2. Open FlipperNativeRpcClient(systemPort) — native protobuf RPC (interface 0)

  3. DetermineActionAsync:
     StorageStatAsync(installPath)
       absent        → action = Install
       MD5 differs   → action = Update
       MD5 matches   → action = Launch

  4. If Install/Update and AutoInstall=false → throw FlipperBootstrapException

  5. UploadFapAsync: StorageMkdirAsync + StorageWriteAsync (512-byte chunks, progress reported)

  6. LaunchFapAsync: native.AppStartAsync(installPath)

  7. native.DisposeAsync()

  8. WaitForDaemonAsync: poll daemonPort every 500 ms (3 s per-attempt timeout)
     until daemon responds to daemon_info, up to DaemonStartTimeout (default 10 s)

  9. Return FlipperBootstrapResult with connected FlipperRpcClient
```

### Bootstrap defaults

| Option                      | Default                                       |
|-----------------------------|-----------------------------------------------|
| `AutoInstall`               | `true`                                        |
| `InstallPath`               | `/ext/apps/Tools/flipper_zero_rpc_daemon.fap` |
| `Timeout`                   | 60 s                                          |
| `DaemonStartTimeout`        | 10 s                                          |
| Daemon poll interval        | 500 ms                                        |
| Per-attempt connect timeout | 3 s                                           |
| Fast-path connect timeout   | 5 s                                           |

---

## Resource Management (C Daemon)

Resources are a `uint32_t` bitmask in `core/rpc_resource.h`. The dispatcher checks and the stream subsystem acquires/releases:

```
rpc_dispatch()
  if cmd->resources != 0 && !resource_can_acquire(cmd->resources)
      → rpc_send_error("resource_busy")  [handler never called]

stream_open()                            [called from *_start handlers]
  slot = stream_alloc_slot()
  resource_acquire(res)                  [sets bits]
  assign stream_id, zero hw union

stream_close_by_index()                  [called from stream_close handler or stream_close_all]
  teardown(slot)                         [hardware cleanup]
  resource_release(resources)            [clears bits]
  active_streams[slot].active = false
```

Non-stream commands that require a resource (e.g. `ir_tx`, `subghz_tx`) are checked by the dispatcher but do **not** call `resource_acquire`/`resource_release` — they hold the hardware for the duration of the handler call only, and the dispatcher's pre-check is the sole enforcement.

Stream commands hold the resource from `stream_open` until `stream_close_by_index`, covering the entire streaming lifetime.
