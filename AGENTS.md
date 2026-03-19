# FlipperZero.NET

## Overview

Monorepo with two sub-projects communicating over USB CDC serial via NDJSON RPC:

| Sub-project | Language | Location | Build |
|---|---|---|---|
| **RPC Daemon** | C (Flipper Zero FAP) | `src/FlipperZeroRpcDaemon/` | `ufbt` |
| **RPC Client** | C# (.NET 8 library) | `src/FlipperZero.NET.Client/` | `dotnet build` |

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
dotnet build   # from repo root; must produce 0 warnings, 0 errors
```

## Project Layout

```
src/FlipperZeroRpcDaemon/
  flipper_zero_rpc_daemon.c          # entry point (~120 lines)
  core/rpc_*.{h,c}                   # protocol infra: transport, dispatch, streams, json, base64, gui, response, resource, cmd_log
  handlers/<subsystem>/<cmd>.{h,c}   # one file pair per command

src/FlipperZero.NET.Client/
  FlipperRpcClient.cs                # core: BoundedChannel, writer/reader loops, SendAsync/SendStreamAsync
  FlipperRpcTransport.cs             # SerialPort wrapper
  FlipperRpcException.cs             # typed exception with ErrorCode
  RpcStream.cs                       # IAsyncEnumerable<TEvent> + IAsyncDisposable
  Abstractions/IRpc*.cs              # IRpcCommand<TResponse>, IRpcStreamCommand<TEvent>, IRpcCommandResponse
  Commands/<Subsystem>/<Cmd>Command.cs   # one file per command/response pair (readonly structs)
  Extensions/Flipper<Subsystem>Extensions.cs  # convenience async methods on FlipperRpcClient

tests/FlipperZero.NET.Client.IntegrationTests/
  <Subsystem>/<Cmd>Tests.cs          # mirrors command structure
  Infrastructure/                    # FlipperFixture, RequiresFlipperFact, StreamTestHelper
```

Subsystem folders: `core`, `system`, `gpio`, `ir`, `subghz`, `nfc`, `notification`, `storage`, `rfid`, `ibutton`.

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

**Response — success**: `{"id":1,"status":"ok","data":{"pong":true}}`
**Response — stream opened**: `{"id":2,"stream":1}`
**Stream event** (unsolicited): `{"event":{...},"stream":1}`
**Response — error**: `{"id":1,"error":"resource_busy"}`

Error codes: `resource_busy`, `unknown_command`, `missing_cmd`, `missing_stream_id`, `stream_not_found`, `stream_table_full`.

### Resource Management

Hardware tracked via `ResourceMask` bitmask (`RESOURCE_SUBGHZ`, `RESOURCE_IR`, `RESOURCE_NFC`, etc.). Dispatcher checks availability before invoking handler. Releasing a stream releases its resources. Max 8 concurrent streams (`MAX_STREAMS`).

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
User code → enqueue RpcWorkItem into BoundedChannel (cap 32)
  ▼
Writer loop → Register() pending state BEFORE send → write JSON line
  ▼
Reader loop → parse JSON → route by "id" or "stream" → complete TCS or push to stream Channel
  ▼
User code receives Task<TResponse> or IAsyncEnumerable<TEvent>
```

---

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
- Outbound messages flow through a single `BoundedChannel<RpcWorkItem>` (cap 32, `SingleReader = true`).
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
3. Implement: parse args with `json_extract_string`/`json_extract_uint32` (`core/rpc_json.h`), respond with `rpc_send_ok()`/`rpc_send_error()` (`core/rpc_response.h`).
4. **Stream commands**: call `stream_alloc_slot()` first (return error if `-1`), then `resource_acquire()`, then send `{"id":N,"stream":M}\n`.

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
       [JsonPropertyName("status")] public string? Status { get; init; }
   }
   ```
2. Add extension method in `Extensions/Flipper<Subsystem>Extensions.cs`:
   ```csharp
   public static Task<MyResponse> MyCommandAsync(this FlipperRpcClient client, CancellationToken ct = default)
       => client.SendAsync<MyCommand, MyResponse>(new MyCommand(), ct);
   ```
3. `dotnet build` — must succeed with 0 warnings.
