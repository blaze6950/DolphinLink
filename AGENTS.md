# FlipperZero.NET

## Overview

Monorepo with three sub-projects. The daemon and client communicate over USB CDC serial via NDJSON RPC; the bootstrapper uses the Flipper's native protobuf RPC to install and launch the daemon before handing off to the client.

| Sub-project | Language | Location | Build |
|---|---|---|---|
| **RPC Daemon** | C (Flipper Zero FAP) | `src/FlipperZeroRpcDaemon/` | `ufbt` |
| **RPC Client** | C# (.NET 8 library) | `src/FlipperZero.NET.Client/` | `dotnet build` |
| **Bootstrapper** | C# (.NET 8 library) | `src/FlipperZero.NET.Bootstrapper/` | `dotnet build` |

## Build

**C Daemon** — requires [ufbt](https://github.com/flipperdevices/flipperzero-ufbt) on `PATH`:
```bash
cd src/FlipperZeroRpcDaemon
python -m ufbt           # build FAP
python -m ufbt launch    # build + deploy over USB
python -m ufbt lint      # clang-tidy
python -m ufbt format    # auto-fix formatting
```

> **LSP false-positives**: C LSP errors for `furi.h`, `bool`, `FuriMessageQueue`,
> etc. are expected — Flipper SDK headers only exist inside the ufbt toolchain.
> The authoritative check is `python -m ufbt`.

**C# Client**:
```bash
dotnet build   # from repo root; must produce 0 warnings, 0 errors (covers Client + Bootstrapper)
```

**Bootstrapper — embedding a freshly-built FAP**:

By default `dotnet build` embeds the pre-committed FAP from
`src/FlipperZero.NET.Bootstrapper/Resources/flipper_zero_rpc_daemon.fap`.

To rebuild the C daemon and automatically copy the new `.fap` into `Resources/`
before compilation, pass `BuildDaemon=true`:

```bash
dotnet build /p:BuildDaemon=true   # builds ufbt FAP first, then compiles C#
```

This requires `ufbt` + Python on `PATH`.  CI must always pass this flag to
ensure the embedded FAP is current.  Local C#-only work can skip it.

## Project Layout

```
src/FlipperZeroRpcDaemon/
  flipper_zero_rpc_daemon.c          # entry point (~120 lines)
  core/rpc_*.{h,c}                   # protocol infra: transport, dispatch, streams, json, base64, gui, response, resource, cmd_log
  handlers/<subsystem>/<cmd>.{h,c}   # one file pair per command

src/FlipperZero.NET.Client/
  FlipperRpcClient.cs                # core RPC logic: SendAsync/SendStreamAsync, reader loop, FaultAll
  FlipperRpcClientOptions.cs         # readonly record struct: HeartbeatInterval + Timeout (default-safe via backing fields)
  RpcLogEntry.cs                     # public diagnostic log entry (RpcLogSource, RpcLogKind enums + RpcLogEntry struct)
  RpcStream.cs                       # IAsyncEnumerable<TEvent> + IAsyncDisposable
  Abstractions/                      # IFlipperTransport, IRpcDiagnostics, IRpcCommand<TResponse>, IRpcStreamCommand<TEvent>, IRpcCommandResponse, IRpcCommandBase
  Commands/<Subsystem>/<Cmd>Command.cs   # one file per command/response pair (readonly structs)
  Commands/Ui/FlipperScreenSession.cs    # exclusive host-driven screen session with draw/flush helpers
  Converters/                        # Base64JsonConverter, HexJsonConverter
  Exceptions/                        # FlipperRpcException (typed exception with ErrorCode), FlipperDisconnectedException (with DisconnectReason enum)
  Extensions/Flipper<Subsystem>Extensions.cs  # convenience async methods on FlipperRpcClient
  Transport/                         # public: SerialPortTransport (SerialPort-backed IFlipperTransport); internal: HeartbeatTransport, PacketSerializationTransport
  Streaming/                         # internal: RpcStreamManager, StreamState, StreamOpenResult
  Dispatch/                          # internal: RpcMessageDispatcher, RpcMessageSerializer, RpcEnvelope, RpcPendingRequests, IPendingRequest, PendingRequest

tests/FlipperZero.NET.Client.UnitTests/
  Infrastructure/FakeTransport.cs    # in-process IFlipperTransport; always use CreateClient(), not new FlipperRpcClient(this)
tests/FlipperZero.NET.Client.HardwareTests/
  <Subsystem>/<Cmd>Tests.cs          # mirrors command structure; requires physical device
  Infrastructure/                    # FlipperFixture, RequiresFlipperFact, StreamTestHelper
tests/FlipperZero.NET.Tests.Infrastructure/
  FlipperFixture.cs                  # shared xUnit collection fixture (FLIPPER_PORT env var)

src/FlipperZero.NET.Bootstrapper/
  FlipperBootstrapper.cs             # public static BootstrapAsync() — full install/launch state machine
  FlipperBootstrapOptions.cs         # readonly record struct: AutoInstall, InstallPath, Timeout, DaemonStartTimeout
  FlipperBootstrapResult.cs          # FlipperBootstrapResult (owns Client), BootstrapAction enum, FlipperBootstrapException
  NativeRpc/
    NativeRpcTransport.cs            # varint-length-delimited protobuf framing over SerialPort (CDC interface 0)
    FlipperNativeRpcClient.cs        # typed native RPC client: PingAsync, StorageStatAsync, StorageMd5SumAsync,
                                     #   StorageMkdirAsync, StorageWriteAsync (chunked), AppStartAsync
  Proto/                             # vendored + trimmed .proto files compiled by Grpc.Tools (message-only, no gRPC services)
    flipper.proto                    # PB.Main envelope + imports
    storage.proto                    # Stat, Md5Sum, Write, Mkdir
    application.proto                # App.StartRequest
    system.proto                     # System.PingRequest/Response
  Resources/
    flipper_zero_rpc_daemon.fap      # pre-built FAP binary embedded as assembly resource
```

Subsystem folders: `core`, `system`, `gpio`, `ir`, `subghz`, `nfc`, `notification`, `storage`, `rfid`, `ibutton`, `ui`, `input`.

`COMMANDS.md` (repo root) — cross-reference table mapping every command name to its C handler file, C# types file, and C# extension method. Keep it in sync when adding or removing commands.

Namespace rule: `FlipperZero.NET.Commands` for request/response types; stream commands use `FlipperZero.NET.Commands.<Subsystem>` (e.g. `FlipperZero.NET.Commands.Ir`).

---

## Protocol: NDJSON over USB CDC

One compact JSON object per line (`\n`-terminated). Never dispatch on `}`.

**Request** (host → Flipper):
```json
{"id":1,"cmd":"ping"}
{"id":2,"cmd":"ir_receive_start"}
{"id":3,"cmd":"stream_close","stream":1}
```

All messages use a compact V3 envelope with single-character keys:

| Field | Type | Role |
|---|---|---|
| `"t"` | int | Type discriminator: `0` = response, `1` = stream event, `2` = daemon exit |
| `"i"` | uint | Request id (on `t:0`) or stream id (on `t:1`) |
| `"p"` | object | Payload; present on success responses and all events |
| `"e"` | string | Error code; present instead of `"p"` on error responses |

**Response — success** (`t:0`, no `"e"`): `{"t":0,"i":1,"p":{"pong":true}}`
**Response — void success** (`t:0`, no `"e"`, no `"p"`): `{"t":0,"i":1}`
**Response — stream opened** (`t:0`): `{"t":0,"i":2,"p":{"stream":1}}`
**Response — error** (`t:0`, has `"e"`): `{"t":0,"i":1,"e":"resource_busy"}`
**Stream event** (`t:1`, unsolicited): `{"t":1,"i":1,"p":{"protocol":"NEC","address":0,"command":0,"repeat":false}}`
**Daemon exit** (`t:2`, unsolicited): `{"t":2}`

Error codes: `resource_busy`, `unknown_command`, `missing_cmd`, `missing_stream_id`, `stream_not_found`, `stream_table_full`, `out_of_memory`, `missing_path`, `open_failed`, `stat_failed`, `storage_error`, `remove_failed`, `mkdir_failed`, `missing_data`, `missing_pin`, `invalid_pin`, `missing_level`, `missing_enable`, `missing_color`, `invalid_color`, `missing_text`, `missing_protocol`, `unknown_protocol`, `missing_timings`, `missing_freq`, `missing_datetime_fields`.

### C# Client mapping

- `SendAsync<TCmd, TResp>()` → sends `{"id":N,"cmd":"..."}`, routes daemon reply by `"i"` on `t:0`.
- `SendStreamAsync<TCmd, TEvent>()` → sends open command, daemon replies `{"t":0,"i":N,"p":{"stream":M}}`; unsolicited `{"t":1,"i":M,"p":{...}}` events follow.
- `RpcStream<T>.DisposeAsync()` → sends `{"id":N,"cmd":"stream_close","stream":M}` to release daemon resources.

### Resource Management (`RESOURCE_SUBGHZ`, `RESOURCE_IR`, `RESOURCE_NFC`, etc.). Dispatcher checks availability before invoking handler. Releasing a stream releases its resources. Max 8 concurrent streams (`MAX_STREAMS`).

---

## Threading Models

**C Daemon:**
```
USB ISR  (cdc_rx_callback)
  │  furi_hal_cdc_receive() → accumulate line → furi_message_queue_put()
  ▼
FuriEventLoop  (main thread)
  │  on_rx_queue → rpc_dispatch() → handler → cdc_send()
  ▼
USB TX
```
All RPC logic runs on the main thread. ISR does only byte accumulation + queue put.

**C# Client:**
```
User code → SendAsync/SendStreamAsync → Register() pending BEFORE send → PacketSerializationTransport
  ▼                                                                       (BoundedChannel, single writer)
Transport stack: SerialPortTransport → PacketSerializationTransport → HeartbeatTransport
  ▼
Reader loop → parse JSON → route by `"t"`: `t:0` → match `"i"` to pending request (complete TCS or raise FlipperRpcException on `"e"`); `t:1` → match `"i"` to stream channel (push `"p"`); `t:2` → FaultAll
  ▼
User code receives Task<TResponse> or IAsyncEnumerable<TEvent>
```

**C# Bootstrapper:**
```
BootstrapAsync()
  │  1. Try ConnectAsync on daemonPortName → if success, return AlreadyRunning
  │  2. Open NativeRpcTransport on systemPortName (CDC interface 0)
  │     PingAsync → StorageStatAsync (check FAP exists) → StorageMd5SumAsync (compare vs bundled MD5)
  │     If missing or MD5 differs: StorageMkdirAsync + StorageWriteAsync (chunked, 512B) → Installed/Updated
  │     AppStartAsync → launch FAP
  │  3. Poll ConnectAsync on daemonPortName until daemon appears (DaemonStartTimeout)
  ▼
Returns FlipperBootstrapResult (owns FlipperRpcClient on CDC interface 1)
```

## Hardware Tests

Hardware and bootstrap tests require a physical Flipper Zero connected over USB.

| Environment variable | CDC interface | Example (Windows) | Example (Linux) |
|---|---|---|---|
| `FLIPPER_PORT` | Interface 1 — NDJSON daemon port | `COM4` | `/dev/ttyACM1` |
| `FLIPPER_SYSTEM_PORT` | Interface 0 — native protobuf RPC port | `COM3` | `/dev/ttyACM0` |

`FLIPPER_PORT` is required for all hardware tests (`RequiresFlipperFact`).
`FLIPPER_SYSTEM_PORT` is additionally required for bootstrap tests (`RequiresBootstrapFact`).

Bootstrap tests run in the `"Flipper bootstrap"` xUnit collection (no shared fixture, own
client per test). They execute sequentially **after** the `"Flipper integration"` collection
finishes, so both ports are free.

## Flipper SDK Rules

These are correctness-critical. LLMs frequently hallucinate the wrong names.

| Rule | Correct | Wrong (do NOT use) |
|---|---|---|
| **ISR safety** | Only `furi_hal_cdc_receive` + `furi_message_queue_put` in `rx_ep_callback`. Never `snprintf`, `FURI_LOG_*`, `furi_hal_cdc_send`, or any blocking call. | — |
| **CDC RX callback** | `void(*)(void* context)` — a *notification*; pull data with `furi_hal_cdc_receive()` inside it. | Treating it as data-delivery callback |
| **Event loop subscribe** | `furi_event_loop_subscribe_message_queue(loop, queue, FuriEventLoopEventIn, cb, ctx)` | ~~`furi_event_loop_message_queue_subscribe`~~ |
| **Event loop unsubscribe** | `furi_event_loop_unsubscribe(loop, queue)` (generic) | ~~`furi_event_loop_message_queue_unsubscribe`~~ |
| **Event loop callback sig** | `void(*)(FuriEventLoopObject* object, void* context)` | ~~returns `bool`~~, ~~takes `FuriMessageQueue*`~~ |
| **CdcCallbacks field** | `.ctrl_line_callback` | ~~`.control_line_callback`~~ |
| **USB config** | Always `usb_cdc_dual` + CDC interface 1 (`RPC_CDC_IF 1`). Save previous config with `furi_hal_usb_get_config()`, restore on exit. Clear CDC callbacks before restoring. | ~~`usb_cdc_single`~~ (hijacks system RPC) |
| **GUI with FuriEventLoop** | `ViewPort` + `gui_add_view_port()` | ~~`ViewDispatcher`~~ (owns its own event loop) |
| **Format specifiers** | `"%" PRIu32` / `"%" PRIx32` from `<inttypes.h>` | ~~`%lu`~~, ~~`%u`~~ (ARM type widths differ) |
| **Stream slot ordering** | Call `stream_alloc_slot()` BEFORE `resource_acquire()` | Acquiring first → ghost resources on slot exhaustion |
| **Register before send (C#)** | `item.Register()` before `SendLineAsync()` in writer loop | Registering after → reader loop race |
| **Client construction (C#)** | `new FlipperRpcClient(transport, options, diagnostics)` — single ctor; `SerialPortTransport` is `public`, callers construct it. `default(FlipperRpcClientOptions)` is safe (backing fields resolve to defaults). | ~~`new FlipperRpcClient(portName)`~~, ~~`new FlipperRpcClient(transport, interval, timeout)`~~ — removed |

---

## C Code Style

- Modules: `core/rpc_<concern>.{h,c}`. Headers expose public API only; internal helpers are `static`.
- Handlers: `handlers/<subsystem>/<cmd>.{h,c}`, one per command. Header declares one function: `void <cmd>_handler(uint32_t id, const char* json);`
- Globals shared across modules defined in `flipper_zero_rpc_daemon.c`, declared `extern` in relevant header.
- `UNUSED(x)` for unused parameters.
- Follow `.clang-format`; enforce with `python -m ufbt lint`.

## C# Code Style

- C# 12, .NET 8, `<Nullable>enable</Nullable>`.
- `ConfigureAwait(false)` on every `await` in library code.
- `sealed` on all classes. `readonly struct` for all command/response types.
- `IAsyncDisposable` on types owning background resources.
- Generic command pattern: `SendAsync<TCommand, TResponse>()` where `TCommand : struct, IRpcCommand<TResponse>` — no boxing, no reflection.
- Public API is extension methods in `Extensions/Flipper<Subsystem>Extensions.cs`.
- Outbound messages flow through a single `BoundedChannel<string>` (cap 32, `SingleReader = true`) inside `PacketSerializationTransport`, not `FlipperRpcClient`.
- Pending requests use type-erased `Action<JsonElement>` closures over typed `TaskCompletionSource<TResponse>`.

---

## Adding a New Command

### C Daemon

1. Create `handlers/<subsystem>/my_cmd.h` + `my_cmd.c`. Header declares:
   ```c
   void my_cmd_handler(uint32_t id, const char* json);
   ```
2. In `core/rpc_dispatch.c`: add `#include` and row to `commands[]`:
   ```c
   {"my_command", RESOURCE_FLAGS, my_cmd_handler},
   ```
3. **Capability negotiation**: add the command name to `SUPPORTED_COMMANDS[]` in `handlers/system/daemon_info.c` so the host detects it via `daemon_info`. If the change is a **breaking wire-format change**, also bump `DAEMON_PROTOCOL_VERSION` in `handlers/system/daemon_info.h`.
4. Implement: parse args with `json_extract_string`/`json_extract_uint32` (`core/rpc_json.h`), respond with `rpc_send_ok()`/`rpc_send_error()` (`core/rpc_response.h`).
5. **Stream commands**: call `stream_alloc_slot()` first (return error if `-1`), then `resource_acquire()`, then send `{"id":N,"stream":M}\n`.

### C# Client

1. Create `Commands/<Subsystem>/MyCommand.cs`:
   ```csharp
   public readonly struct MyCommand : IRpcCommand<MyResponse>
   {
       public string CommandName => "my_command";
       public void WriteArgs(Utf8JsonWriter writer) { /* ... */ }
   }

   public readonly struct MyResponse : IRpcCommandResponse
   {
       [JsonPropertyName("my_field")] public string? MyField { get; init; }
   }
   ```
2. Add extension method in `Extensions/Flipper<Subsystem>Extensions.cs`:
   ```csharp
   public static Task<MyResponse> MyCommandAsync(this FlipperRpcClient client, CancellationToken ct = default)
       => client.SendAsync<MyCommand, MyResponse>(new MyCommand(), ct);
   ```
3. Add integration tests in `tests/FlipperZero.NET.Client.HardwareTests/<Subsystem>/MyCommandTests.cs` covering at minimum: happy-path round-trip, resource conflict (if applicable), and stream open/close lifecycle (for stream commands). Follow the `[Collection(FlipperCollection.Name)]`, `[RequiresFlipperFact]`, `[Trait("Category", "Hardware")]` conventions.
4. `dotnet build` — must succeed with 0 warnings.
