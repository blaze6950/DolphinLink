# DolphinLink — Agent Guide

## Overview

Monorepo with three sub-projects. The daemon runs on a Flipper Zero and exposes an NDJSON RPC interface over USB CDC. The client is a .NET library that speaks that protocol. The bootstrapper uses the Flipper's native protobuf RPC to install and launch the daemon.

| Sub-project | Language | Path | Build |
|---|---|---|---|
| **RPC Daemon** | C (Flipper Zero FAP) | `src/DolphinLinkRpcDaemon/` | `python -m ufbt` |
| **RPC Client** | C# (.NET 8 library) | `src/DolphinLink.Client/` | `dotnet build` |
| **Bootstrapper** | C# (.NET 8 library) | `src/DolphinLink.Bootstrapper/` | `dotnet build` |

---

## Build

### C Daemon

Requires [ufbt](https://github.com/flipperdevices/flipperzero-ufbt) on `PATH`. Run from `src/DolphinLinkRpcDaemon/`:

```bash
python -m ufbt          # build FAP
python -m ufbt lint     # clang-tidy
python -m ufbt format   # auto-fix formatting
```

> **LSP false-positives**: C LSP errors for `furi.h`, `bool`, `FuriMessageQueue`, etc. are expected — Flipper SDK headers only exist inside the ufbt toolchain. The authoritative check is `python -m ufbt`.

### C# Solution

```bash
dotnet build            # from repo root; builds Client + Bootstrapper
```

CI passes `-warnaserror`. Treat all warnings as errors; zero warnings is required.

To rebuild the C daemon FAP and embed it in the Bootstrapper: `dotnet build /p:BuildDaemon=true`.

### Deploy script

`deploy-daemon.csx` automates build → upload → launch. Run `dotnet tool restore` once, then `dotnet script deploy-daemon.csx`. Use `-- --no-build` to skip C rebuild, `-- --system COM5 --daemon COM6` to override ports.

---

## Test

```bash
dotnet test --filter "FullyQualifiedName~UnitTests"                  # no hardware needed; CI runs this
$env:FLIPPER_PORT="COM4"; dotnet test --filter "FullyQualifiedName~HardwareTests"  # needs Flipper
```

Set `FLIPPER_SYSTEM_PORT` too for bootstrap tests. Or use `.\test-report.ps1 -Category Hardware` for an HTML report.

| Project | Purpose |
|---|---|
| `tests/DolphinLink.Client.UnitTests/` | All new logic; uses `FakeTransport` (in-process) |
| `tests/DolphinLink.Client.HardwareTests/` | End-to-end on real hardware |
| `tests/DolphinLink.Client.ManualTests/` | Interactive / exploratory only |
| `tests/DolphinLink.Tests.Infrastructure/` | Shared fixtures and attributes (not runnable) |

**Conventions:** `RequiresDeviceFact` / `RequiresBootstrapFact` attributes. `[Trait("Category","Hardware")]` on all hardware tests. Method naming: `MethodName_Scenario_ExpectedBehavior`. Cover at minimum: happy-path, resource conflict (if applicable), stream open/close lifecycle.

---

## Codegen

Schemas in `schema/` are the **single source of truth**. Never edit generated files (`.g.cs`, `rpc_dispatch_generated.h`).

```bash
dotnet script codegens/codegen.csx      # C# → src/DolphinLink.Client/Generated/
dotnet script codegens/c-codegen.csx    # C  → src/DolphinLinkRpcDaemon/generated/rpc_dispatch_generated.h
dotnet build
```

Schema layout: `schema/command-registry.json` (ID → name, append-only), `schema/commands/<subsystem>/<cmd>.json`, `schema/streams/<name>.json`, `schema/enums/<Name>.json`, `schema/resources.json`. See `SCHEMA.md` for the full field-by-field format reference.

Some C# types use **partial structs** — a generated `.g.cs` alongside a hand-written `.cs` (e.g. `DaemonInfoResponse.Supports()`, `ConfigureCommand`). Add custom logic in the hand-written partial; never modify the `.g.cs`.

---

## Adding a New Command

### Step 1 — Schema

**a.** Append to `schema/command-registry.json` (integer key = wire command ID, append-only, never reuse).

**b.** Create `schema/commands/<subsystem>/<cmd>.json`. Copy `schema/commands/gpio/gpio_read.json` (simple) or `schema/commands/ir/ir_tx.json` (resource + enum) as a template. For streams, copy `schema/streams/ir_receive.json`; use `"stream"` instead of `"command"`, `"event"` instead of `"response"`, and register with a `_start` suffix.

**Schema field types:** `bool`, `int`, `uint`, `byte`, `string`, `bytes` (base64), `hex`, `uint[]`, `string[]`. Enum: `"enum": "$EnumName"`. Use `"csharp": { "skip": ["command","response","extension"] }` for hand-written types.

### Step 2 — Codegen

```bash
dotnet script codegens/c-codegen.csx    # updates dispatch table + COMMAND_NAMES[]
dotnet script codegens/codegen.csx      # updates C# types + extensions
```

### Step 3 — C handler

Create `handlers/<subsystem>/<cmd>.{h,c}`. Follow any existing handler as a template (e.g. `handlers/gpio/gpio_read.{h,c}`). The handler signature is always:

```c
void <cmd>_handler(uint32_t id, const char* json, size_t offset);
```

Parse args with `json_find()` + `json_value_*()`, respond with `rpc_send_ok()` / `rpc_send_data_response()` / `rpc_send_error()`.

**For stream commands**, call `stream_open()` first (it allocates a slot, acquires the resource, and sends the opened response):

```c
uint32_t stream_id = 0;
int slot = stream_open(id, "my_stream_start", RESOURCE_FOO, &stream_id);
if(slot < 0) return;  // stream_open already sent the error
```

### Step 4 — C# (if codegen is insufficient)

Add a hand-written extension in `Extensions/Flipper<Subsystem>Extensions.cs` using `client.SendAsync<TCmd, TResp>(...)`. Follow existing hand-written extensions as templates.

### Step 5 — Build & verify

```bash
python -m ufbt          # from src/DolphinLinkRpcDaemon/
dotnet build            # from repo root; 0 warnings, 0 errors
```

Bump `DAEMON_PROTOCOL_VERSION` in `handlers/system/daemon_info.h` only for **breaking wire-format changes**.

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
| **Stream slot ordering** | Call `stream_open()` — it atomically allocates a slot then acquires the resource. | Acquiring resource before slot → ghost resources on slot exhaustion |
| **Register before send (C#)** | `item.Register()` before `SendLineAsync()` in writer loop | Registering after → reader loop race |
| **Client construction (C#)** | `new RpcClient(transport)` or `new RpcClient(transport, options, diagnostics)` — `options` and `diagnostics` are optional with safe defaults. | ~~`new RpcClient(portName)`~~, ~~`new RpcClient(transport, interval, timeout)`~~ |

---

## C Code Style

- **Modules:** `core/rpc_<concern>.{h,c}`. **Handlers:** `handlers/<subsystem>/<cmd>.{h,c}`, one per command.
- **Unused params:** `(void)json; (void)offset;`
- **Globals:** Defined in `dolphin_link_rpc_daemon.c`, `extern` in headers. Shared handles in `core/rpc_globals.h`.
- **Headers:** `#pragma once`. **Format:** `<inttypes.h>` macros (`PRIu32`, etc.), never `%lu`/`%u`.
- **clang-format:** 4-space indent, column limit 99, LF. Enforce with `python -m ufbt format` / `lint`.

**Response helpers** (`core/rpc_response.h`): `rpc_send_ok(id, cmd)`, `rpc_send_data_response(id, json_obj, cmd)`, `rpc_send_error(id, code, cmd)`.

**JSON parsing** (`core/rpc_json.h`): Zero-copy forward scan. Pass `offset` as initial hint, advance via `val.offset`:

```c
JsonValue val;
if(!json_find(json, "pr", offset, &val)) { rpc_send_error(...); return; }
json_value_string(&val, buf, sizeof(buf));
if(json_find(json, "a", val.offset, &val)) json_value_uint32(&val, &address);
```

---

## C# Code Style

- C# 12, .NET 8, `<Nullable>enable</Nullable>`. `ConfigureAwait(false)` on every `await`.
- `sealed` classes. `readonly struct` for command/response/event types. `IAsyncDisposable` for resource owners.
- Generic pattern: `SendAsync<TCommand, TResponse>()` where `TCommand : struct, IRpcCommand<TResponse>`.
- Public API: extension methods in `Extensions/Flipper<Subsystem>Extensions.cs`.
- Namespaces: `DolphinLink.Client.Commands`, stream events in `DolphinLink.Client.Commands.<Subsystem>`.
- `RpcClientOptions`: `default` is safe (`HeartbeatInterval=3s`, `Timeout=10s`).
- Exceptions: `RpcException` → `DisconnectedException`.

---

## Project Layout

**Subsystems:** `core`, `system`, `gpio`, `ir`, `subghz`, `nfc`, `notification`, `storage`, `rfid`, `ibutton`, `ui`, `input`.

Key paths not obvious from the directory tree:
- `src/DolphinLink.Client/Commands/<Sub>/` — hand-written partial structs alongside `Generated/Commands/<Sub>/`
- `src/DolphinLink.Bootstrapper/Resources/dolphin_link_rpc_daemon.fap` — pre-built FAP embedded at compile time
- `tests/DolphinLink.Tests.Infrastructure/` — `DeviceFixture`, `FakeTransport`, skip attributes

`PROTOCOL.md` — wire format, envelope fields, error codes, message examples.
`ARCHITECTURE.md` — threading models, transport stack, bootstrapper flow.
