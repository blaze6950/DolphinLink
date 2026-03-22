# FlipperZero.NET — Wire Protocol

The daemon and client communicate over **USB CDC** (interface 1) using **newline-delimited JSON** (NDJSON). Each message
is a single UTF-8 JSON object terminated by `\n`. There are no binary framing layers.

---

## Envelope Format

### Requests (host → daemon)

```
{"c":<cmd_id>,"i":<request_id>,...args...}\n
```

| Field    | Type    | Description                                         |
|----------|---------|-----------------------------------------------------|
| `c`      | integer | Command ID from `schema/command-registry.json`      |
| `i`      | uint32  | Request ID; echoed in the response for correlation  |
| _(args)_ | varies  | Command-specific fields written inline (not nested) |

### Responses (daemon → host)

**Success, void:**

```json
{"t":0,"i":1}
```

**Success with data:**

```json
{"t":0,"i":1,"p":{"key":"value"}}
```

**Error:**

```json
{"t":0,"i":1,"e":"error_code"}
```

**Stream opened:**

```json
{"t":0,"i":1,"p":{"s":7}}
```

`"s"` is the stream ID assigned by the daemon; used to correlate subsequent events and the `stream_close` command.

### Stream events (daemon → host, unsolicited)

```json
{"t":1,"i":7,"p":{"key":"value"}}
```

`"i"` here is the **stream ID** (not a request ID). Events are unsolicited and arrive out-of-band from responses.

### Daemon exit (daemon → host)

```json
{"t":2}
```

Sent by the daemon immediately before teardown (when the user exits the FAP or DTR drops). No `"i"` field.

### Keep-alive (bidirectional)

```
\n
```

A bare newline (empty line). Sent by either side when TX has been idle for `heartbeat_interval` ms. Both sides silently
ignore received keep-alives.

---

## Message Type Discriminator

| `"t"` value | Meaning                     |
|-------------|-----------------------------|
| `0`         | Response (success or error) |
| `1`         | Stream event                |
| `2`         | Daemon exit                 |

---

## Command IDs

Commands are numbered sequentially from 0. The authoritative mapping is `schema/command-registry.json`. Current
commands (IDs 0–45):

| ID | Name                   | ID | Name                 |
|----|------------------------|----|----------------------|
| 0  | `ping`                 | 23 | `nfc_scan_start`     |
| 1  | `stream_close`         | 24 | `led_set`            |
| 2  | `configure`            | 25 | `led_set_rgb`        |
| 3  | `daemon_info`          | 26 | `vibro`              |
| 4  | `daemon_stop`          | 27 | `speaker_start`      |
| 5  | `device_info`          | 28 | `speaker_stop`       |
| 6  | `power_info`           | 29 | `backlight`          |
| 7  | `datetime_get`         | 30 | `storage_info`       |
| 8  | `datetime_set`         | 31 | `storage_list`       |
| 9  | `region_info`          | 32 | `storage_read`       |
| 10 | `frequency_is_allowed` | 33 | `storage_write`      |
| 11 | `reboot`               | 34 | `storage_mkdir`      |
| 12 | `gpio_read`            | 35 | `storage_remove`     |
| 13 | `gpio_write`           | 36 | `storage_stat`       |
| 14 | `adc_read`             | 37 | `lfrfid_read_start`  |
| 15 | `gpio_set_5v`          | 38 | `ibutton_read_start` |
| 16 | `gpio_watch_start`     | 39 | `input_listen_start` |
| 17 | `ir_tx`                | 40 | `ui_screen_acquire`  |
| 18 | `ir_tx_raw`            | 41 | `ui_screen_release`  |
| 19 | `ir_receive_start`     | 42 | `ui_draw_str`        |
| 20 | `subghz_tx`            | 43 | `ui_draw_rect`       |
| 21 | `subghz_get_rssi`      | 44 | `ui_draw_line`       |
| 22 | `subghz_rx_start`      | 45 | `ui_flush`           |

---

## Request/Response Examples

### `ping` (ID 0)

```
→ {"c":0,"i":1}
← {"t":0,"i":1,"p":{"pg":1}}
```

### `gpio_read` (ID 12)

```
→ {"c":12,"i":2,"p":1}
← {"t":0,"i":2,"p":{"lv":1}}
```

### `ir_tx` (ID 17) — resource-guarded, void response

```
→ {"c":17,"i":3,"pr":"NEC","a":0,"cm":0}
← {"t":0,"i":3}
```

### `ir_receive_start` (ID 19) — stream command

```
→ {"c":19,"i":4}
← {"t":0,"i":4,"p":{"s":1}}                              ← stream opened, stream_id=1
← {"t":1,"i":1,"p":{"pr":"NEC","a":0,"cm":42,"rp":0}}
← {"t":1,"i":1,"p":{"pr":"NEC","a":0,"cm":42,"rp":1}}
→ {"c":1,"i":5,"s":1}                                    ← stream_close (s = stream_id)
← {"t":0,"i":5}
```

### Error response

```
→ {"c":17,"i":6,"pr":"NEC","a":0,"cm":0}
← {"t":0,"i":6,"e":"resource_busy"}
```

---

## Error Codes

| Code                | Source             | Meaning                                          |
|---------------------|--------------------|--------------------------------------------------|
| `missing_cmd`       | dispatcher         | `"c"` field absent or unparseable                |
| `unknown_command`   | dispatcher         | `cmd_id >= CMD_COUNT`                            |
| `resource_busy`     | dispatcher         | Required resource already held by another stream |
| `stream_table_full` | `stream_open`      | All 8 stream slots are occupied                  |
| `missing_pin`       | gpio handlers      | `"p"` (pin) field absent                         |
| `invalid_pin`       | gpio handlers      | Pin number out of range                          |
| `missing_protocol`  | ir/subghz handlers | `"pr"` field absent                              |
| `unknown_protocol`  | ir/subghz handlers | Protocol name not recognised                     |
| `missing_timings`   | `ir_tx_raw`        | `"tm"` (timings array) absent                    |
| `missing_path`      | storage handlers   | `"p"` (path) field absent                        |
| `open_failed`       | storage handlers   | File open error                                  |
| `storage_error`     | storage handlers   | General storage subsystem error                  |

---

## Stream Lifecycle

1. Host sends a `*_start` command (e.g. `ir_receive_start`).
2. Daemon allocates a slot, acquires the resource, assigns a `stream_id`, and responds with
   `{"t":0,"i":<req_id>,"p":{"s":<stream_id>}}`.
3. Hardware events arrive as `{"t":1,"i":<stream_id>,"p":{...}}`.
4. Host closes the stream via `stream_close` (ID 1) with `"s":<stream_id>`. The daemon tears down hardware, releases the
   resource, frees the slot, and responds void-success.
5. If the host disconnects (DTR drop or heartbeat timeout), the daemon closes all streams and resets all resources
   automatically.

Maximum concurrent streams: **8** (`MAX_STREAMS` in `core/rpc_stream.h`).

---

## Resources

Some commands require exclusive hardware access. The dispatcher enforces this before invoking any handler; the handler
is never called if the resource is busy.

| Resource           | Held by                                                           |
|--------------------|-------------------------------------------------------------------|
| `RESOURCE_IR`      | `ir_tx`, `ir_tx_raw`, `ir_receive_start`                          |
| `RESOURCE_SUBGHZ`  | `subghz_tx`, `subghz_get_rssi`, `subghz_rx_start`                 |
| `RESOURCE_NFC`     | `nfc_scan_start`                                                  |
| `RESOURCE_SPEAKER` | `speaker_start`                                                   |
| `RESOURCE_RFID`    | `lfrfid_read_start`                                               |
| `RESOURCE_IBUTTON` | `ibutton_read_start`                                              |
| `RESOURCE_GUI`     | `ui_screen_acquire`, `ui_screen_release`, `ui_draw_*`, `ui_flush` |

Stream commands hold their resource for the lifetime of the stream. Non-stream commands (e.g. `ir_tx`) hold it only
during handler execution.

---

## Capability Negotiation

After connecting, the host sends `daemon_info` (ID 3). The daemon responds with its name, protocol version, and
supported command names:

```
→ {"c":3,"i":1}
← {"t":0,"i":1,"p":{"n":"flipper_zero_rpc_daemon","v":1,"cmds":["ping","stream_close",...]}}
```

The host verifies `"n"` and `"v"` before proceeding. If `configure` appears in `"cmds"`, the host sends a `configure`
command to negotiate heartbeat timing and the LED indicator colour.

---

## Heartbeat

Both sides send a bare `\n` when TX has been idle for `heartbeat_interval` ms (default 3 s). If a side receives no bytes
within `timeout` ms (default 10 s), it treats the connection as lost. The daemon closes all streams and resets resource
state on timeout.

The constraint `timeout > heartbeat_interval` is enforced by both the C daemon and the C# `HeartbeatTransport`.

---

## Wire Limits

| Limit                 | Value                     |
|-----------------------|---------------------------|
| Max request line      | 1024 bytes (incl. `\n`)   |
| Max USB packet        | 64 bytes                  |
| Daemon TX buffer      | 512 bytes (stream buffer) |
| Stream event fragment | 128 bytes                 |
