# FlipperZero.NET

## Overview

A monorepo containing two tightly coupled sub-projects that communicate over USB CDC serial using a JSON-based RPC protocol:

| Sub-project | Language | Location | Build tool |
|---|---|---|---|
| **RPC Daemon** | C (Flipper Zero FAP) | `src/FlipperZeroRpcDaemon/` | `ufbt` (micro Flipper Build Tool) |
| **RPC Client** | C# (.NET 8 class library) | `src/FlipperZero.NET.Client/` | `dotnet build` |

The Flipper runs the daemon as an on-device app. The laptop runs the C# client, connecting over the USB cable (CDC virtual COM port).

---

## Project Structure

```
FlipperZero.NET/
├── FlipperZero.NET.sln          # .NET solution (client only — C daemon is not an MSBuild project)
├── global.json                  # Pins .NET SDK 8.0.x (rollForward: latestMinor)
├── AGENTS.md                    # This file
│
├── src/
│   ├── FlipperZeroRpcDaemon/               # Flipper Zero C application
│   │   ├── flipper_zero_rpc_daemon.c       # Entry point: globals, on_rx_queue(), app main
│   │   ├── rpc_resource.h                  # Header-only: ResourceMask type, acquire/release/reset
│   │   ├── rpc_json.h / rpc_json.c         # JSON extraction helpers (json_extract_string/uint32)
│   │   ├── rpc_transport.h / rpc_transport.c  # RxLine, cdc_send(), cdc_rx_callback() ISR
│   │   ├── rpc_cmd_log.h / rpc_cmd_log.c   # Ring-buffer command log, cmd_log_push/reset
│   │   ├── rpc_response.h / rpc_response.c # rpc_send_ok/error/response() — shared response helpers
│   │   ├── rpc_stream.h / rpc_stream.c     # RpcStream table, alloc/find/close/reset helpers
│   │   ├── rpc_dispatch.h / rpc_dispatch.c # RpcCommand struct, commands[] table, rpc_dispatch()
│   │   ├── rpc_handlers.h / rpc_handlers.c # ping, ir_receive_start, gpio_watch_start, subghz_rx_start, nfc_scan_start, stream_close handlers
│   │   ├── rpc_gui.h / rpc_gui.c           # AppState, draw/input callbacks, setup/teardown
│   │   ├── application.fam                 # Flipper app manifest (entry point, stack 4KB, icon)
│   │   ├── flipper_zero_rpc_daemon.png     # 10x10 1-bit icon
│   │   ├── images/                         # Image assets compiled into FAP
│   │   ├── .github/workflows/build.yml     # CI: builds FAP via ufbt for dev + release SDK channels
│   │   ├── .clang-format                   # C formatting rules
│   │   └── .vscode/                        # VS Code settings, compile_commands.json (ufbt-generated)
│   │
│   └── FlipperZero.NET.Client/             # C# RPC client library
│       ├── FlipperZero.NET.Client.csproj   # Targets net8.0; depends on System.IO.Ports 8.0.0
│       ├── Abstractions/
│       │   ├── IRpcCommand.cs              # IRpcCommand<TResponse> — request/response pairing
│       │   ├── IRpcStreamCommand.cs        # IRpcStreamCommand<TEvent> — stream-opening commands
│       │   └── IRpcCommandResponse.cs      # Marker interface for all response/event structs
│       ├── Commands/
│       │   └── RpcCommands.cs              # All command + response structs (Ping, BleScanStart, StreamClose)
│       ├── FlipperRpcClient.cs             # Core: BoundedChannel, writer loop, reader loop, SendAsync/SendStreamAsync
│       ├── FlipperRpcClient.Commands.cs    # Public convenience API: PingAsync(), BleScanStartAsync(), etc.
│       ├── FlipperRpcTransport.cs          # SerialPort wrapper with async line-oriented I/O
│       ├── FlipperRpcException.cs          # Typed exception with ErrorCode and RequestId
│       └── RpcStream.cs                    # IAsyncEnumerable<TEvent> + IAsyncDisposable stream handle
```

---

## Build Instructions

### C Daemon (Flipper)

Requires [ufbt](https://github.com/flipperdevices/flipperzero-ufbt) installed and on `PATH`.

```bash
cd src/FlipperZeroRpcDaemon
python -m ufbt           # Build FAP only
python -m ufbt launch    # Build + deploy to connected Flipper over USB
python -m ufbt lint      # Run clang-tidy
```

CI builds against both `dev` and `release` SDK channels on every push and daily
(see `.github/workflows/build.yml`). Build artifacts are uploaded as GitHub
Actions artifacts.

> **LSP false-positives**: The local C LSP will show errors for `furi.h`,
> `bool`, `true`/`false`, `FuriMessageQueue`, etc. because the Flipper SDK
> headers only exist inside the ufbt toolchain, not on the host machine. These
> are expected and do not indicate real problems. The authoritative check is
> `python -m ufbt`.

### C# Client

```bash
# from repo root
dotnet build                             # build full solution
dotnet build src/FlipperZero.NET.Client  # build client only
```

The solution uses .NET 8 with `<Nullable>enable</Nullable>` and
`<ImplicitUsings>enable</ImplicitUsings>`. The build must produce **0 warnings**
and **0 errors**.

---

## Architecture

### Transport

USB CDC — the Flipper daemon takes over USB as a CDC device; it appears as a
virtual COM port (`COM3`, `/dev/ttyACM0`, etc.) on the host. No drivers needed
on modern operating systems.

### Protocol: NDJSON

One JSON object per line, terminated with `\n` only. Never trigger dispatch on
`}`. No pretty-printing; all messages are compact single-line JSON.

### Message Formats

**Request** (host → Flipper):
```json
{"id":1,"cmd":"ping"}
{"id":2,"cmd":"ir_receive_start"}
{"id":3,"cmd":"stream_close","stream":1}
```

**Response — success**:
```json
{"id":1,"status":"ok","data":{"pong":true}}
```

**Response — stream opened**:
```json
{"id":2,"stream":1}
```

**Stream event** (unsolicited, after stream open):
```json
{"event":{"addr":"AA:BB:CC:DD:EE:FF","rssi":-70},"stream":1}
```

**Response — error**:
```json
{"id":1,"error":"resource_busy"}
```

Known error codes: `resource_busy`, `unknown_command`, `missing_cmd`,
`missing_stream_id`, `stream_not_found`, `stream_table_full`.

### Resource Management

The daemon tracks in-use hardware with a `ResourceMask` bitmask
(`RESOURCE_SUBGHZ`, `RESOURCE_IR`, `RESOURCE_NFC`). Each command declares which
resources it needs. The dispatcher checks availability before invoking the
handler. Releasing a stream releases its resources. Up to 8 concurrent streams
are supported (`MAX_STREAMS`). `RESOURCE_BLE` (bit 0) is reserved but unused —
BLE GAP observer is not exposed in the FAP SDK.

### C Daemon Threading Model

```
USB ISR  (cdc_rx_callback — rpc_transport.c)
  │  pulls data via furi_hal_cdc_receive()
  │  accumulates bytes into line buffer
  │  on '\n': pushes RxLine into FuriMessageQueue  [non-blocking, may drop]
  ▼
FuriEventLoop  (main thread — flipper_zero_rpc_daemon.c)
  │  on_rx_queue fires when queue becomes readable
  │  calls rpc_dispatch() (rpc_dispatch.c) → handler (rpc_handlers.c) → cdc_send() (rpc_transport.c)
  ▼
USB TX  (furi_hal_cdc_send)
```

All RPC logic (JSON parsing, dispatch, resource management, response
formatting) runs on the main thread. The ISR does only byte accumulation and
`furi_message_queue_put`.

### C# Client Threading Model

```
User code
  │  calls PingAsync() / BleScanStartAsync()
  │  enqueues RpcWorkItem into BoundedChannel<RpcWorkItem>  (capacity 32)
  ▼
Writer loop  (single background Task)
  │  dequeues work items sequentially
  │  registers pending state BEFORE sending  ← prevents reader-loop race
  │  sends JSON line via FlipperRpcTransport
  ▼
Reader loop  (single background Task)
  │  reads lines from serial port
  │  parses JSON, routes by "id" (request) or "stream" (event)
  │  completes TaskCompletionSource<TResponse> or pushes into stream Channel
  ▼
User code receives result / iterates IAsyncEnumerable<TEvent>
```

---

## C Daemon Conventions

### Flipper SDK Patterns — Critical Rules

| Rule | Detail |
|---|---|
| **ISR safety** | `rx_ep_callback` runs in USB interrupt context. Only `furi_hal_cdc_receive`, `furi_message_queue_put`, and `furi_stream_buffer_send` are safe to call. Never call `snprintf`, `FURI_LOG_*`, `furi_hal_cdc_send`, or any blocking API from ISR. |
| **CDC RX callback signature** | `void(*)(void* context)` — it is a *notification* that data is available, NOT a data-delivery callback. Call `furi_hal_cdc_receive(if_num, buf, sizeof(buf))` inside it to pull data. |
| **Event loop API** | Subscribe: `furi_event_loop_subscribe_message_queue(loop, queue, FuriEventLoopEventIn, cb, ctx)`. Unsubscribe: `furi_event_loop_unsubscribe(loop, queue)` (generic). The incorrect names `furi_event_loop_message_queue_subscribe` / `furi_event_loop_message_queue_unsubscribe` do not exist. |
| **Event loop callback signature** | `void(*)(FuriEventLoopObject* object, void* context)` — returns `void`, first parameter is `FuriEventLoopObject*`. |
| **CdcCallbacks field names** | `tx_ep_callback`, `rx_ep_callback`, `state_callback`, `ctrl_line_callback`, `config_callback`. The field is `ctrl_line_callback`, NOT `control_line_callback`. |
| **GUI with FuriEventLoop** | Use `ViewPort` + `gui_add_view_port()`. Do NOT use `ViewDispatcher` — it owns its own event loop and conflicts with `FuriEventLoop`. |
| **USB lifecycle** | Save the previous USB config with `furi_hal_usb_get_config()` before calling `furi_hal_usb_set_config(&usb_cdc_dual, NULL)`. Use CDC interface 1 (`RPC_CDC_IF 1`) for app traffic — interface 0 is the system RPC used by qFlipper and must never be touched. Restore the previous config on exit. Clear CDC callbacks on interface 1 before restoring. Never use `usb_cdc_single` — it hijacks the system RPC channel. |
| **Format specifiers** | Use `"%" PRIu32` / `"%" PRIx32` from `<inttypes.h>` for `uint32_t`. Never use `%lu` or `%u` — ARM Cortex-M type widths differ from host. |

### C Code Style

- Multi-file module architecture: daemon logic is split across focused `rpc_*.h`/`rpc_*.c`
  modules. `flipper_zero_rpc_daemon.c` is the entry point only (~120 lines).
- Each module is named `rpc_<concern>` (e.g. `rpc_transport`, `rpc_dispatch`,
  `rpc_handlers`). All modules expose only what their header declares; internal
  helpers are `static`.
- Flipper allman-adjacent style: braces on the same line for control flow.
  Follow `.clang-format` for formatting; `python -m ufbt lint` enforces it.
  Auto-fix with `python -m ufbt format`.
- Use `UNUSED(x)` for unused parameters.
- All module-level variables are `static`; globals shared across modules are
  defined in `flipper_zero_rpc_daemon.c` and declared `extern` in the relevant
  header (e.g. `active_resources` in `rpc_resource.h`, `rx_queue` in
  `rpc_transport.h`).
- Forward-declare handlers before the command registry table in `rpc_dispatch.c`.

---

## C# Client Conventions

### Design Patterns

**Generic command pattern, no boxing**
`SendAsync<TCommand, TResponse>(TCommand command)` where
`TCommand : struct, IRpcCommand<TResponse>`. The command type is passed as a
generic type parameter — never as an interface — so no boxing occurs.

**Command-response pairing via interfaces**
`IRpcCommand<TResponse>` pairs each command struct with its response type.
`IRpcStreamCommand<TEvent>` pairs stream commands with their event type.
All implementations are `readonly struct`.

**BoundedChannel for serialised writes**
All outbound messages flow through a single `Channel<RpcWorkItem>` (capacity
32, `SingleReader = true`). This guarantees no interleaving on the wire and
provides a natural backpressure point.

**Typed TCS captured in a closure**
Each pending request owns a `TaskCompletionSource<TResponse>`. Because
`_pending` must store heterogeneous types, the TCS is captured in an
`Action<JsonElement> OnSuccess` closure that deserialises and completes it.
`PendingRequest` stores only type-erased callbacks — no reflection, no boxing
of the TCS.

**Register before send**
The writer loop calls `item.Register()` to populate `_pending` or `_streams`
before sending the JSON line. This prevents a race where the reader loop
receives a response before the pending entry is registered.

**Partial class split**
`FlipperRpcClient.cs` — internal mechanics (generic `SendAsync`,
`SendStreamAsync`, loops, routing).
`FlipperRpcClient.Commands.cs` — public surface (`PingAsync`,
`BleScanStartAsync`, `StreamCloseAsync`). Adding a new command requires
only touching `Commands/RpcCommands.cs` and `FlipperRpcClient.Commands.cs`.

### Namespace Layout

| Namespace | Contents |
|---|---|
| `FlipperZero.NET` | `FlipperRpcClient`, `FlipperRpcException`, `FlipperRpcTransport`, `RpcStream<T>` |
| `FlipperZero.NET.Abstractions` | `IRpcCommand<T>`, `IRpcStreamCommand<T>`, `IRpcCommandResponse` |
| `FlipperZero.NET.Commands` | `PingCommand`, `PingResponse`, `IrReceiveStartCommand`, `IrReceiveEvent`, `GpioWatchStartCommand`, `GpioWatchEvent`, `SubGhzRxStartCommand`, `SubGhzRxEvent`, `NfcScanStartCommand`, `NfcScanEvent`, `StreamCloseCommand`, `StreamCloseResponse` |

### Dependencies

| Package | Version | Use |
|---|---|---|
| `System.Text.Json` | built-in | All JSON serialisation / deserialisation |
| `System.IO.Ports` | 8.0.0 (NuGet) | Serial port access |
| `System.Threading.Channels` | built-in | Internal `BoundedChannel` queuing |

### C# Code Style

- C# 12, .NET 8. `<Nullable>enable</Nullable>`.
- `ConfigureAwait(false)` on every `await` in library code.
- `sealed` on all classes not designed for inheritance.
- `readonly struct` for all command and response types.
- `IAsyncDisposable` on types that own unmanaged or background resources.
- No `public` constructors on command structs that require arguments — use
  primary-constructor-style `readonly struct MyCommand(uint id)`.

---

## Adding a New Command

### Step 1 — C Daemon

1. Declare the handler in `rpc_handlers.h` and implement it in `rpc_handlers.c`:
   ```c
   static void my_cmd_handler(uint32_t id, const char* json);
   ```
2. Add a row to the `commands[]` table in `rpc_dispatch.c`:
   ```c
   {"my_command", RESOURCE_FLAGS, my_cmd_handler},
   ```
3. Implement the handler using `json_extract_string` / `json_extract_uint32`
   (from `rpc_json.h`) for argument parsing and `rpc_send_ok()` / `rpc_send_error()`
   (from `rpc_response.h`) for responses.
4. For stream commands: check `stream_alloc_slot()` before calling
   `resource_acquire()`, store the slot, then send `{"id":N,"stream":M}\n`.

### Step 2 — C# Client

1. Add structs to `Commands/RpcCommands.cs`:
   ```csharp
   public readonly struct MyCommand : IRpcCommand<MyResponse>
   {
       public string CommandName => "my_command";
       public void WriteArgs(Utf8JsonWriter writer) { /* write args */ }
   }

   public readonly struct MyResponse : IRpcCommandResponse
   {
       [JsonPropertyName("status")] public string? Status { get; init; }
   }
   ```
2. Add a convenience method to `FlipperRpcClient.Commands.cs`:
   ```csharp
   public Task<MyResponse> MyCommandAsync(CancellationToken ct = default)
       => SendAsync<MyCommand, MyResponse>(new MyCommand(), ct);
   ```
3. Run `dotnet build` — must succeed with 0 warnings.

---

## Common Pitfalls

| Pitfall | Cause | Fix |
|---|---|---|
| ISR context violation | Calling logging, snprintf, or TX inside `rx_ep_callback` | Only `furi_hal_cdc_receive` + `furi_message_queue_put` in callback |
| CDC callback wrong signature | Treating `rx_ep_callback` as data-delivery | Signature is `void(*)(void*)` — pull data with `furi_hal_cdc_receive()` |
| Using `usb_cdc_single` | Hijacks CDC interface 0 (system RPC), breaking qFlipper | Always use `usb_cdc_dual` and CDC interface 1 (`RPC_CDC_IF 1`) for app traffic |
| Wrong event loop subscribe name | Using `furi_event_loop_message_queue_subscribe` | Correct: `furi_event_loop_subscribe_message_queue` |
| Wrong event loop unsubscribe name | Using `furi_event_loop_message_queue_unsubscribe` | Correct: `furi_event_loop_unsubscribe` (generic, works for all object types) |
| Wrong event loop callback signature | Returning `bool`, taking `FuriMessageQueue*` | Must return `void`, first param is `FuriEventLoopObject*` |
| Wrong CdcCallbacks field name | `.control_line_callback` | Correct: `.ctrl_line_callback` |
| `%lu` / `%u` format mismatch | Platform-specific type widths | Always `"%" PRIu32` for `uint32_t` |
| Stream slot exhaustion + ghost resources | Acquiring resources before checking for a free slot | Call `stream_alloc_slot()` first; return error if `-1` |
| CS0411 generic type inference | Phantom type parameter in helper method not inferrable from args | Use `Action<Utf8JsonWriter>` delegate instead of `<TCommand, TResponse>` on serialiser helpers |
| Reader loop races registration | Registering pending state after sending | Always call `item.Register()` before `SendLineAsync()` in the writer loop |
| LSP errors in C file | Missing Flipper SDK headers on host | Expected; verify with `python -m ufbt`, not the LSP |

---

## Future Work

- **JSON schema → codegen**: Define commands in a `.json` schema; generate the
  C command registry and C# command/response structs from it automatically.
- **Client-side resource tracking**: Short-circuit `resource_busy` errors on
  the host without a round-trip, mirroring the daemon's bitmask logic.
- **Real BLE scanning**: Wire up `furi_hal_bt_*` APIs in
  `ble_scan_start_handler` to emit real scan events instead of a stub.
- **Additional commands**: SubGHz, IR, GPIO, NFC — each follows the same
  two-step pattern above.
- **NuGet packaging**: Publish `FlipperZero.NET.Client` as a NuGet package.
- **Integration tests**: Test harness that drives a real or emulated Flipper
  and validates the full round-trip.
