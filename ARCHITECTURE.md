# DolphinLink — Architecture

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

The C# client layers transports on top of an `ISerialPort` abstraction. On desktop the port is
`SystemSerialPort` (wrapping `System.IO.Ports.SerialPort`); in a Blazor WASM browser the port is
`WebSerialPort` (wrapping the browser's WebSerial API). Two optional decorator layers sit between
the port and the client; both are skipped in WASM (see [WASM transport options](#wasm-transport-options)):

```
ISerialPort
  ├── SystemSerialPort             (System.IO.Ports.SerialPort — desktop/server)
  └── WebSerialPort                (browser WebSerial API — Blazor WASM only)
         ↓
SerialPortTransport                (ISerialPort → ITransport adapter)
         ↑
PacketSerializationTransport       (optional — skipped when DisablePacketSerialization=true)
  single-writer via BoundedChannel<string>(32) + background WriterLoopAsync
         ↑
HeartbeatTransport                 (optional — skipped when DisableHeartbeat=true)
  bidirectional keep-alive + RX timeout watchdog
         ↑
RpcClient                   (RPC request/response and stream logic)
```

Each layer implements `ITransport`:

```csharp
public interface ITransport : IAsyncDisposable
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
- A background `WriterLoopAsync` task (started via `Task.Run`) drains the channel and calls `_inner.SendAsync`.
- `ReceiveAsync` forwards directly to the inner transport.
- `DisposeAsync` order: seal channel → cancel writer CTS → dispose inner transport → await writer task. The inner dispose comes first to unblock any pending `WriteLineAsync` on Windows.
- **Disabled in WASM** (`DisablePacketSerialization = true`) — see [WASM transport options](#wasm-transport-options).

### HeartbeatTransport

- Sends a keep-alive (`string.Empty` → bare `\n` on wire) when TX has been idle for `heartbeatInterval` (default 3 s).
- Tracks last-sent and last-seen timestamps using `Interlocked.Exchange` on `long` tick values.
- Fires `event Action? Disconnected` exactly once (guarded by `Interlocked.CompareExchange`) when the RX timeout expires or a send fails. `RpcClient` wires this to `FaultAll(HeartbeatTimeout)`.
- `ReceiveAsync` updates `_lastSeenTicks` on every line including daemon keep-alives, then `continue`s on whitespace-only lines so they are invisible to the layer above.
- The constraint `timeout > heartbeatInterval` is enforced in the constructor.
- **Disabled in WASM** (`DisableHeartbeat = true`) — see [WASM transport options](#wasm-transport-options).

---

## WebSerial Transport (Blazor WASM)

`DolphinLink.WebSerial` provides a browser-native `ISerialPort` implementation that wraps the
[Web Serial API](https://developer.mozilla.org/en-US/docs/Web/API/Web_Serial_API). It is only
available in Chromium-based browsers (Chrome ≥ 89, Edge ≥ 89). Check
`WebSerialHelpers.IsSupported()` before showing any port-picker UI.

### Key types

| Type                  | Role                                                                  |
|-----------------------|-----------------------------------------------------------------------|
| `WebSerialPort`       | `ISerialPort` implementation; wraps a JS port handle integer          |
| `WebSerialStream`     | `Stream` adapter; bridges the JS ReadableStream pump to .NET `Stream` |
| `WebSerialPortPicker` | Static helpers: picker UI, system-port teardown, auto-connect         |
| `WebSerialInterop`    | Internal `[JSImport]`/`[JSExport]` bindings to `webserial-interop.js` |
| `WebSerialHelpers`    | `IsSupported()` / `IsSupportedAsync()` browser-detection helpers      |

### Port lifecycle

```
WebSerialPort.CreateAsync(vid, pid, baud)
  └─ JSHost.ImportAsync → loads webserial-interop.js ES module
  └─ InitModuleJs       → resolves [JSExport] OnData via getAssemblyExports
  └─ OpenPortJs         → navigator.serial.requestPort() → port.open()
  └─ returns WebSerialPort(portId)          portId = JS Map key (integer)

port.OpenAsync()
  └─ new WebSerialStream(portId)
       └─ RegisterCallback(portId, OnDataReceived)
       └─ StartReadingJs(portId)            starts JS ReadableStream pump

port.DisposeAsync()
  └─ stream.DisposeAsync()                 completes channel → unblocks ReadAsync
  └─ ClosePortJs(portId)                   port.close() in JS (keeps permission)

port.ForgetAsync()                         (instead of DisposeAsync when re-enumerating)
  └─ stream.DisposeAsync()
  └─ ForgetPortJs(portId)                  port.close() + port.forget() in JS
```

### Read path (JS → .NET)

```
JS ReadableStream pump (async loop in webserial-interop.js)
  └─► WebSerialInterop.OnData(portId, byte[])     [JSExport] static method
        └─► _callbacks[portId](data)              per-port dispatch table
              └─► WebSerialStream.OnDataReceived()
                    non-empty → channel.Writer.TryWrite(data)
                    empty     → channel.Writer.TryComplete()   (EOF sentinel)

WebSerialStream.ReadAsync()
  └─► channel.Reader.WaitToReadAsync()            blocks until data or EOF
  └─► CopyFromCurrent()                           drains chunk into caller's buffer
```

### Write path (.NET → JS)

```
WebSerialStream.WriteAsync(buffer)
  └─► WebSerialInterop.WriteJs(portId, byte[])    [JSImport]
        └─► JS port WritableStream.write(data)
```

### JS handle lifecycle and _closedPorts map

The JS module (`webserial-interop.js`) tracks open ports in a `_ports` Map keyed by integer
handle. `closePort()` moves the raw `SerialPort` object into `_closedPorts` (capped at 32 entries,
oldest evicted first) before releasing it from `_ports`. `forgetPort()` checks `_closedPorts` as a
fallback so it can still call `SerialPort.forget()` even after `DisposeAsync` has already run — this
is required by the bootstrapper flow where the system port is disposed before `ForgetAsync` fires.

### Bootstrapper flow (first visit)

```
1. Button click → PickSystemPortAsync()     shows picker → CDC 0 (system/native RPC)
2. Bootstrapper.BootstrapAsync(...)
3. onBeforeDaemonConnect callback:
   a. ForgetSystemPortAsync(systemPort)     closes + forgets CDC 0, releases USB claim
   b. Task.Delay(reEnumerationDelay)        wait for Flipper to re-enumerate as dual-CDC
   c. show "Pick daemon port" button → UI button click → PickAnyPortAsync() → CDC 1
   d. SignalDaemonPortReady(tcs, daemonPort)
   e. WaitForDaemonPortAsync(tcs) returns   bootstrap resumes with daemon port
4. Bootstrapper returns BootstrapResult with connected RpcClient
```

The `CreateDaemonPortWaiter()` / `WaitForDaemonPortAsync()` / `SignalDaemonPortReady()` trio
implements a `TaskCompletionSource` bridge between the bootstrap async flow (which cannot call
`requestPort()` without a user gesture) and the UI button click (which can).

### Subsequent visits (auto-connect)

```
OnInitializedAsync → TryAutoConnectAsync()
  └─ GetPortsJs(vid, pid, baud)            navigator.serial.getPorts() — no gesture needed
  └─ for each portId: probe ConnectAsync with 3 s timeout
       success → return RpcClient (daemon port CDC 1)
       failure → DisposeAsync client + port, try next
  └─ null → show normal "Connect" button
```

### Threading notes

Blazor WASM is single-threaded (cooperative). All JS callbacks arrive on the same thread as .NET
code. The `BoundedChannel<byte[]>(64)` in `WebSerialStream` provides back-pressure across
`await` yield points only; `TryWrite` is used in `OnDataReceived` rather than `WriteAsync` to avoid
re-entrancy on the cooperative thread.

### WASM transport options

**Always set both of these when using `WebSerialPort` in a Blazor WASM application:**

```csharp
var options = new RpcClientOptions
{
    DisablePacketSerialization = true,
    DisableHeartbeat           = true,
};
```

#### Why `DisablePacketSerialization = true`

`PacketSerializationTransport` uses `Task.Run(() => WriterLoopAsync(...))` to run a background
writer loop. In WASM, `Task.Run` does not spawn an OS thread — it schedules another cooperative
task on the single-threaded JS event loop. This creates a second competing `await foreach` loop
alongside the WebSerial JS read pump (`reader.read()` callbacks via `[JSExport] OnData`). The extra
competing loop adds cooperative-scheduler contention with no benefit: there is no thread-level
concurrency in WASM, so there are no concurrent `SendAsync` callers to serialize. Setting
`DisablePacketSerialization = true` completely removes the transport layer and its writer task —
`SendAsync` calls go directly to the inner transport with zero overhead.

#### Why `DisableHeartbeat = true`

`HeartbeatTransport` also uses `Task.Run` for its heartbeat loop, which runs `await Task.Delay(...)`
on a fixed interval. Two problems arise in a browser:

1. **Cooperative scheduler starvation.** The heartbeat loop competes with the WebSerial JS read
   pump on the single-threaded cooperative scheduler. Even with the `await Task.Yield()` that the
   loop inserts after each `Task.Delay`, the pump can be starved long enough that the heartbeat
   loop's own RX watchdog (`(now − lastSeen) > timeout`) fires on an otherwise healthy connection.

2. **Browser tab throttling.** When a tab is backgrounded, browsers throttle `setTimeout`
   (which backs `Task.Delay`) to intervals of ≥ 1 second, sometimes much longer. If throttling
   delays the heartbeat loop by more than the 10-second RX timeout, `TriggerDisconnect()` fires,
   which calls `FaultAll(HeartbeatTimeout)` — failing every in-flight request with
   `DisconnectedException` and cancelling `client.Disconnected` on a perfectly healthy
   connection.

Setting `DisableHeartbeat = true` has three coordinated effects:

- `HeartbeatTransport` is never instantiated — no background loop, no timer, no `Disconnected`
  event subscription.
- `NegotiateAsync` sends a `configure` command with `heartbeatMs = 3,600,000` (1 h) and
  `timeoutMs = 7,200,000` (2 h), so the **daemon's own RX watchdog** never fires on the host side
  either. (Requires daemon protocol version ≥ 4; throws `RpcException` otherwise.)
- `ReaderLoopAsync` filters out bare `\n` keep-alive frames that now reach the reader directly
  (since `HeartbeatTransport` is no longer in the chain to consume them).

The daemon continues to send its own keep-alive `\n` frames every 3 seconds regardless; these are
simply discarded by the reader loop filter. Connection loss in a browser is immediately visible to
the user, so transport-level liveness probing is unnecessary.

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

`RpcStream<TEvent>` is an `IAsyncEnumerable<TEvent>` backed by a `ChannelReader<JsonElement>`. Events are deserialized lazily during iteration. On disconnect, the stream surfaces `DisconnectedException` rather than `OperationCanceledException`.

### Disconnect and fault propagation

`FaultAll(DisconnectedException)` is idempotent (Interlocked guard):
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

`RpcJsonNormalizer` (namespace `DolphinLink`) transforms compact wire-format JSON into
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

`Bootstrapper.BootstrapAsync` connects a physical Flipper Zero and ensures the daemon FAP is installed and running.

```
BootstrapAsync(systemPort, daemonPort)
  1. Fast path: TryConnectDaemonDirectAsync (5 s timeout)
     └─ success → return AlreadyRunning (daemon already running)

  2. Open NativeRpcClient(systemPort) — native protobuf RPC (interface 0)

  3. DetermineActionAsync:
     StorageStatAsync(installPath)
       absent        → action = Install
       MD5 differs   → action = Update
       MD5 matches   → action = Launch

  4. If Install/Update and AutoInstall=false → throw BootstrapException

  5. UploadFapAsync: StorageMkdirAsync + StorageWriteAsync (512-byte chunks, progress reported)

  6. LaunchFapAsync: native.AppStartAsync(installPath)

  7. native.DisposeAsync()

  8. WaitForDaemonAsync: poll daemonPort every 500 ms (3 s per-attempt timeout)
     until daemon responds to daemon_info, up to DaemonStartTimeout (default 10 s)

  9. Return BootstrapResult with connected RpcClient
```

### Bootstrap defaults

| Option                      | Default                                       |
|-----------------------------|-----------------------------------------------|
| `AutoInstall`               | `true`                                        |
| `InstallPath`               | `/ext/apps/Tools/dolphin_link_rpc_daemon.fap` |
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
