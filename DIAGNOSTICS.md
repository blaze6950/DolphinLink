# Diagnostics

FlipperZero.NET has two complementary diagnostics layers: client-side round-trip measurement (always active, zero wire overhead) and daemon-side per-request phase timing (opt-in, inlined into responses).

---

## Client-Side Diagnostics (`IRpcDiagnostics`)

The C# client logs every sent command and every received response/stream event without any additional protocol overhead. Implement `IRpcDiagnostics` and pass it to the `FlipperRpcClient` constructor to receive these entries:

```csharp
public interface IRpcDiagnostics
{
    void Log(RpcLogEntry entry);
}
```

Each `RpcLogEntry` contains:

| Property      | Type           | Description                                                                                                                       |
|---------------|----------------|-----------------------------------------------------------------------------------------------------------------------------------|
| `Source`      | `RpcLogSource` | Always `Client`                                                                                                                   |
| `Kind`        | `RpcLogKind`   | `CommandSent`, `ResponseReceived`, `StreamEventReceived`, or `Error`                                                              |
| `RequestId`   | `uint?`        | Request ID, or `null` for stream events                                                                                           |
| `StreamId`    | `uint?`        | Stream ID, or `null` for request/response entries                                                                                 |
| `CommandName` | `string?`      | Command name (e.g. `"gpio_read"`), if known                                                                                       |
| `Status`      | `string?`      | `"ok"`, an error code, or `null` for `CommandSent`                                                                                |
| `RawJson`     | `string?`      | Full raw JSON line (request or response)                                                                                          |
| `Elapsed`     | `TimeSpan`     | Monotonic time since client connected                                                                                             |
| `RoundTrip`   | `TimeSpan?`    | For `ResponseReceived`: time from command sent to response received (full observable round-trip including USB transfer both ways) |

`RpcLogEntry.ToString()` produces a compact single-line summary suitable for structured logging.

### Usage

```csharp
var client = new FlipperRpcClient(transport, diagnostics: new MyDiagnostics());
```

### Implementation contract

Implementations **must** be synchronous, non-blocking, and **must not** throw. Log to a channel, `ILogger`, or a `ConcurrentQueue` and process asynchronously if needed.

---

## Daemon-Side Per-Request Timing (`"dx"` / `"_m"`)

When enabled, the daemon appends a `"_m"` (metrics) object to every `"t":0` response envelope, measuring elapsed milliseconds for each internal processing phase.

### Wire format

```json
{"t":0,"i":5,"p":{"level":true},"_m":{"pr":1,"dp":0,"ex":3,"sr":0,"tt":4}}
```

| Key  | Phase         | What is measured                                                       |
|------|---------------|------------------------------------------------------------------------|
| `pr` | **parse**     | JSON forward-scan to extract `"c"` (command ID) and `"i"` (request ID) |
| `dp` | **dispatch**  | Bounds-check, `COMMAND_NAMES` lookup, resource pre-check               |
| `ex` | **execute**   | Handler invocation — arg parsing, hardware interaction, payload build  |
| `sr` | **serialize** | Response envelope formatting (`snprintf` in `rpc_send_*()`)            |
| `tt` | **total**     | Entry to `rpc_dispatch()` through `cdc_send()` (end-to-end)            |

All values are in **milliseconds** (`furi_get_tick()` resolution, ~1 ms on Flipper Zero).

**Stream events (`"t":1`) are not instrumented** — they originate from hardware ISR callbacks, not the request/response dispatch path.

### When metrics are zero

`pr` and `dp` often show `0` for simple commands (GPIO read, ping) because JSON parsing and dispatch take less than 1 ms. This is accurate, not a bug — `furi_get_tick()` has ~1 ms granularity.

### Enabling daemon diagnostics

Set `DaemonDiagnostics = true` in `FlipperRpcClientOptions`:

```csharp
var client = new FlipperRpcClient(transport, new FlipperRpcClientOptions
{
    DaemonDiagnostics = true,
});
```

This causes the client to send `"dx":true` in the `configure` handshake command immediately after `daemon_info`. The daemon stores the flag for the duration of the session and appends `"_m"` to every response.

### Accessing the data

The `"_m"` field is present in `RpcLogEntry.RawJson` and is visible to any `IRpcDiagnostics` implementation. Standard response types (e.g. `GpioReadResponse`) do not have a `_m` property — `System.Text.Json` silently ignores unknown fields during deserialization.

To extract metrics from the raw JSON:

```csharp
void Log(RpcLogEntry entry)
{
    if (entry.Kind != RpcLogKind.ResponseReceived || entry.RawJson is null) return;
    using var doc = JsonDocument.Parse(entry.RawJson);
    if (!doc.RootElement.TryGetProperty("_m", out var m)) return;

    var total = m.GetProperty("tt").GetUInt32();
    var execute = m.GetProperty("ex").GetUInt32();
    Console.WriteLine($"[{entry.CommandName}] total={total}ms execute={execute}ms RT={entry.RoundTrip?.TotalMilliseconds:F1}ms");
}
```

### Session lifecycle

The `"dx"` flag is scoped to a single connection:

- Default: `false` (disabled) on every new connection.
- Enabled: by sending `"dx":true` in `configure` (done automatically when `DaemonDiagnostics = true`).
- Reset: the daemon clears `metrics_enabled = false` on every disconnect (DTR drop or RX watchdog timeout), regardless of whether the host disconnected cleanly.

### Performance impact

When disabled (`DaemonDiagnostics = false`, the default):
- **Zero overhead** — no `furi_get_tick()` calls, no `snprintf` for `"_m"`, no extra bytes on the wire.

When enabled:
- 4 × `furi_get_tick()` calls per request (in dispatch) + 1 × `furi_get_tick()` call per response (in serialize).
- ~60 bytes additional wire payload per response (`,"_m":{"pr":N,"dp":N,"ex":N,"sr":N,"tt":N}`).

### Wire protocol detail

The `"dx"` field in the `configure` request uses **PATCH semantics**: omitting `"dx"` leaves the current value unchanged (it remains `false` by default). Only `"dx":true` needs to be sent — the client never sends `"dx":false` explicitly.

The `configure` response always echoes the effective `"dx"` value so the host can confirm the daemon understood the request:

```json
{"t":0,"i":2,"p":{"hb":3000,"to":10000,"led":{"r":81,"g":43,"b":212},"dx":true}}
```

---

## Daemon-Side C Implementation

The metrics infrastructure lives in `src/FlipperZeroRpcDaemon/core/`:

| File                          | Role                                                                                   |
|-------------------------------|----------------------------------------------------------------------------------------|
| `core/rpc_metrics.h`          | `RpcMetrics` struct, `metrics_enabled` extern, `metrics_append()` inline helper        |
| `core/rpc_transport.c`        | Storage for `metrics_enabled` and `g_metrics`; reset in `heartbeat_reset_config()`     |
| `core/rpc_transport.h`        | `extern` declarations; includes `rpc_metrics.h`                                        |
| `core/rpc_dispatch.c`         | Captures `t_start`, `t_parsed`, `t_dispatched`, `t_handler_done` via `furi_get_tick()` |
| `core/rpc_response.c`         | Calls `metrics_append()` in all three `rpc_send_*()` helpers                           |
| `handlers/system/configure.c` | Parses `"dx"` field, stores in `metrics_enabled`, echoes in response                   |
