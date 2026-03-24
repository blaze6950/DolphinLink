# FlipperZero.NET — Schema Reference

The `schema/` directory is the **single source of truth** for the entire protocol. Both the C
daemon (dispatch table, `COMMAND_NAMES[]`) and the C# client (command/response/event structs,
extension methods, JSON normalizer) are fully code-generated from these files. Never edit
generated files directly — change the schema and re-run codegen.

---

## Directory layout

```
schema/
├── command-registry.json        — authoritative ID → name mapping (append-only)
├── resources.json               — list of exclusive hardware resource names
├── commands/<subsystem>/<cmd>.json   — one file per request/response command
├── streams/<name>.json          — one file per streaming command
└── enums/<Name>.json            — one file per enum type
```

---

## `command-registry.json`

Maps integer command IDs to wire names. This is the **only place** IDs are assigned.

```json
{
  "0": "ping",
  "1": "stream_close",
  "12": "gpio_read",
  ...
}
```

**Rules:**
- Keys are **sequential integers** starting at `0`, represented as JSON strings.
- The registry is **append-only**. Never remove or reorder entries; never reuse an ID.
- The integer value of the key is what goes in `"c"` on the wire.
- Stream commands are registered here with a `_start` suffix (e.g. `"16": "gpio_watch_start"`).

---

## `resources.json`

A flat array of resource name strings. Each name becomes a `RESOURCE_*` constant in the C
daemon and is referenced by name in command/stream schemas.

```json
["RESOURCE_IR", "RESOURCE_SUBGHZ", "RESOURCE_NFC", "RESOURCE_SPEAKER", "RESOURCE_RFID", "RESOURCE_IBUTTON", "RESOURCE_GUI"]
```

Resources represent exclusive hardware access. The dispatcher enforces them as a `uint32_t`
bitmask — only one holder at a time, checked before any handler is called.

---

## Command schemas (`schema/commands/<subsystem>/<cmd>.json`)

Describes a single request/response command. The subsystem directory name is a lowercase hint
for file organisation; the `"subsystem"` field inside the file controls C# namespace placement.

### Minimal example — `gpio_read.json`

```json
{
  "command": "gpio_read",
  "subsystem": "Gpio",
  "description": "Read digital GPIO pin state",
  "resource": null,
  "request": {
    "pin": { "wire": "p", "type": "int", "enum": "$GpioPin", "description": "GPIO pin" }
  },
  "response": {
    "level": { "wire": "lv", "type": "bool", "description": "Pin level (true=high)" }
  },
  "errors": ["missing_pin", "invalid_pin"],
  "extensionReturn": { "type": "bool", "field": "level" }
}
```

### With resource and enum — `ir_tx.json`

```json
{
  "command": "ir_tx",
  "subsystem": "Ir",
  "description": "Transmit an IR signal using a known protocol",
  "resource": "RESOURCE_IR",
  "request": {
    "protocol": { "wire": "pr", "type": "string", "enum": "$IrProtocol", "description": "IR protocol" },
    "address":  { "wire": "a",  "type": "uint",                          "description": "Device address" },
    "command":  { "wire": "cm", "type": "uint",                          "description": "IR command code" }
  },
  "response": {},
  "errors": ["resource_busy", "missing_protocol", "unknown_protocol"],
  "extensionReturn": { "type": "void" }
}
```

### With optional fields — `configure.json`

```json
{
  "command": "configure",
  "subsystem": "System",
  "request": {
    "heartbeat_ms": { "wire": "hb", "type": "uint", "optional": true, "description": "..." },
    "timeout_ms":   { "wire": "to", "type": "uint", "optional": true, "description": "..." }
  },
  "response": {},
  "errors": [],
  "extensionReturn": { "type": "void" },
  "csharp": { "skip": ["command", "response", "extension"] }
}
```

### With a `bytes` field — `storage_read.json`

```json
{
  "command": "storage_read",
  "subsystem": "Storage",
  "request":  { "path": { "wire": "p", "type": "string", "description": "File path" } },
  "response": { "data": { "wire": "d", "type": "bytes",  "description": "File contents (base64-encoded)" } },
  "errors": ["missing_path", "open_failed", "storage_error"],
  "extensionReturn": { "type": "byte[]", "field": "data" }
}
```

### With a `uint[]` field — `subghz_tx.json`

```json
{
  "command": "subghz_tx",
  "subsystem": "SubGhz",
  "resource": "RESOURCE_SUBGHZ",
  "request": {
    "freq":    { "wire": "fr", "type": "uint",   "description": "Frequency in Hz" },
    "timings": { "wire": "tm", "type": "uint[]", "description": "Alternating mark/space timings in µs" }
  },
  "response": {},
  "errors": ["resource_busy", "missing_freq", "missing_timings"],
  "extensionReturn": { "type": "void" }
}
```

### Top-level fields

| Field               | Required | Description                                                                                                  |
|---------------------|----------|--------------------------------------------------------------------------------------------------------------|
| `"command"`         | yes      | Wire command name; must match the value in `command-registry.json`                                           |
| `"subsystem"`       | yes      | PascalCase subsystem name; controls C# namespace (`FlipperZero.NET.Commands.<Subsystem>`) and directory hint |
| `"description"`     | yes      | Human-readable description (used in generated XML docs)                                                      |
| `"resource"`        | yes      | `null`, or a string from `resources.json` (e.g. `"RESOURCE_IR"`)                                             |
| `"request"`         | yes      | Map of field name → field descriptor (may be `{}` for no args)                                               |
| `"response"`        | yes      | Map of field name → field descriptor (may be `{}` for void response)                                         |
| `"errors"`          | yes      | Array of error code strings this command can return                                                          |
| `"extensionReturn"` | yes      | Controls the C# extension method return type (see below)                                                     |
| `"csharp"`          | no       | Optional codegen override (see below)                                                                        |

### Field descriptor

Every entry in `"request"` and `"response"` is a field descriptor:

| Property        | Required | Description                                                                                                    |
|-----------------|----------|----------------------------------------------------------------------------------------------------------------|
| `"wire"`        | yes      | Abbreviated key used on the wire (e.g. `"lv"`, `"pr"`)                                                         |
| `"type"`        | yes      | Field type — see type table below                                                                              |
| `"description"` | yes      | Human-readable description                                                                                     |
| `"optional"`    | no       | `true` → field may be omitted from the request; generates `?`-typed C# property                                |
| `"enum"`        | no       | `"$EnumName"` → field is constrained to values of that enum; C# uses the enum type, C validates the string/int |

### Field types

| Type       | Wire encoding            | C# type         | Notes                                                |
|------------|--------------------------|-----------------|------------------------------------------------------|
| `bool`     | `0` / `1` (integer)      | `bool`          | JSON numeric boolean, not `true`/`false`             |
| `int`      | JSON integer             | `int`           | Signed 32-bit                                        |
| `uint`     | JSON integer             | `uint`          | Unsigned 32-bit                                      |
| `byte`     | JSON integer             | `byte`          | 0–255                                                |
| `string`   | JSON string              | `string`        | UTF-8                                                |
| `string[]` | JSON array of strings    | `string[]`      |                                                      |
| `uint[]`   | JSON array of integers   | `uint[]`        | Used for timing arrays                               |
| `bytes`    | JSON string (base64)     | `byte[]`        | Binary data; codegen adds encode/decode              |
| `hex`      | JSON string (hex digits) | `string`        | No `0x` prefix; treated as opaque string in C#       |
| `object`   | JSON object              | `JsonElement`   | Complex/variable shape; hand-written deserialization |
| `object[]` | JSON array of objects    | `JsonElement[]` | e.g. `storage_list` entries                          |

### `"extensionReturn"`

Controls the signature of the generated C# extension method on `FlipperRpcClient`:

| Shape                                             | C# method signature                                                                 |
|---------------------------------------------------|-------------------------------------------------------------------------------------|
| `{ "type": "void" }`                              | `Task ConfigureAsync(...)`                                                          |
| `{ "type": "bool", "field": "level" }`            | `Task<bool> GpioReadAsync(...)` — unwraps `response.Level`                          |
| `{ "type": "byte[]", "field": "data" }`           | `Task<byte[]> StorageReadAsync(...)` — unwraps `response.Data`                      |
| `{ "type": "DeviceInfoResponse", "field": null }` | `Task<DeviceInfoResponse> DeviceInfoAsync(...)` — returns the whole response struct |

### `"csharp"` override

Suppresses code generation for specific parts of a command when you need hand-written types:

```json
"csharp": { "skip": ["command", "response", "extension"] }
```

| Value         | Effect                                        |
|---------------|-----------------------------------------------|
| `"command"`   | Don't generate the request struct (`.g.cs`)   |
| `"response"`  | Don't generate the response struct (`.g.cs`)  |
| `"extension"` | Don't generate the extension method (`.g.cs`) |

Omitting `"csharp"` entirely generates all three. Any combination is valid. The hand-written
partial lives alongside the generated file in `src/FlipperZero.NET.Client/Commands/<Subsystem>/`.

---

## Stream schemas (`schema/streams/<name>.json`)

Describes a streaming command — one that opens a long-lived channel and emits repeated events
until closed. Stream schemas use `"stream"` and `"event"` instead of `"command"` and `"response"`.

The command is always registered in `command-registry.json` with a `_start` suffix
(e.g. `"gpio_watch"` → `"gpio_watch_start"`).

### Minimal example — `gpio_watch.json`

```json
{
  "stream": "gpio_watch",
  "subsystem": "Gpio",
  "description": "Watch a GPIO pin for level changes",
  "resource": null,
  "request": {
    "pin": { "wire": "p", "type": "int", "enum": "$GpioPin", "description": "GPIO pin to watch" }
  },
  "event": {
    "pin":   { "wire": "p",  "type": "int",  "enum": "$GpioPin", "description": "Pin that changed" },
    "level": { "wire": "lv", "type": "bool",                     "description": "New pin level" }
  }
}
```

### With resource — `ir_receive.json`

```json
{
  "stream": "ir_receive",
  "subsystem": "Ir",
  "resource": "RESOURCE_IR",
  "request": {},
  "event": {
    "protocol": { "wire": "pr", "type": "string", "enum": "$IrProtocol", "description": "IR protocol" },
    "address":  { "wire": "a",  "type": "uint",                          "description": "Device address" },
    "command":  { "wire": "cm", "type": "uint",                          "description": "IR command code" },
    "repeat":   { "wire": "rp", "type": "bool",                          "description": "True if repeat signal" }
  }
}
```

### With optional request fields — `input_listen.json`

```json
{
  "stream": "input_listen",
  "subsystem": "Input",
  "resource": null,
  "request": {
    "exit_key":  { "wire": "ek", "type": "int", "enum": "$FlipperInputKey",  "optional": true, "description": "Key that auto-closes the stream" },
    "exit_type": { "wire": "et", "type": "int", "enum": "$FlipperInputType", "optional": true, "description": "Event type that triggers auto-close" }
  },
  "event": {
    "key":  { "wire": "k",  "type": "int", "enum": "$FlipperInputKey",  "description": "Input key" },
    "type": { "wire": "ty", "type": "int", "enum": "$FlipperInputType", "description": "Event type" }
  }
}
```

### Top-level fields

| Field           | Required | Description                                                                            |
|-----------------|----------|----------------------------------------------------------------------------------------|
| `"stream"`      | yes      | Base stream name without `_start` suffix; must match the registry value minus `_start` |
| `"subsystem"`   | yes      | Same as command schemas                                                                |
| `"description"` | yes      | Same as command schemas                                                                |
| `"resource"`    | yes      | `null` or a resource name; held for the entire lifetime of the stream                  |
| `"request"`     | yes      | Fields sent in the `*_start` command (may be `{}`)                                     |
| `"event"`       | yes      | Fields present in every `{"t":1,...}` event message                                    |

Codegen produces:
- A `<Name>Stream.g.cs` containing the `<Name>Event` readonly struct.
- An extension method `<Name>StartAsync(...)` returning `IAsyncEnumerable<<Name>Event>`.

---

## Enum schemas (`schema/enums/<Name>.json`)

Defines a named enumeration. The name becomes a C# `enum` and is referenced in field
descriptors as `"$<Name>"`.

### Integer-wire enum — `GpioPin.json`

```json
{
  "name": "GpioPin",
  "wireType": "int",
  "baseType": "byte",
  "values": [
    { "name": "Pin1", "wire": 1 },
    { "name": "Pin2", "wire": 2 }
  ]
}
```

### String-wire enum — `IrProtocol.json`

```json
{
  "name": "IrProtocol",
  "wireType": "string",
  "values": [
    { "name": "NEC",  "wire": "NEC"  },
    { "name": "RC5",  "wire": "RC5"  }
  ]
}
```

### Top-level fields

| Field        | Required | Description                                                                  |
|--------------|----------|------------------------------------------------------------------------------|
| `"name"`     | yes      | PascalCase enum name; referenced as `"$<Name>"` in field descriptors         |
| `"wireType"` | yes      | `"int"` or `"string"` — how values travel on the wire                        |
| `"baseType"` | no       | C# underlying type for integer enums (e.g. `"byte"`). Omit for string enums. |
| `"values"`   | yes      | Array of `{ "name": "<PascalCase>", "wire": <int or string> }` entries       |

**`wireType` effects:**
- `"int"` — the field is sent as a JSON integer on the wire; C validates range, C# uses the enum type directly.
- `"string"` — the field is sent as a JSON string on the wire; C and C# both do name ↔ value translation.

---

## Codegen pipeline

Three scripts in `codegens/` consume the schemas. Run them after any schema change, then build:

```bash
dotnet script codegens/c-codegen.csx       # C daemon: updates rpc_dispatch_generated.h
dotnet script codegens/codegen.csx         # C# client: updates Generated/ structs + extensions
dotnet build                               # compile; must produce 0 warnings, 0 errors
```

A third script updates the JSON normalizer used in the diagnostics console:

```bash
dotnet script codegens/normalizer-codegen.csx   # C#: updates RpcJsonNormalizer.g.cs
```

### What each codegen produces

| Script                   | Output                                                        | Contents                                                                                                         |
|--------------------------|---------------------------------------------------------------|------------------------------------------------------------------------------------------------------------------|
| `c-codegen.csx`          | `src/FlipperZeroRpcDaemon/generated/rpc_dispatch_generated.h` | Dispatch table array (handler function pointers, resource bitmask, stream flag) + `COMMAND_NAMES[]` string array |
| `codegen.csx`            | `src/FlipperZero.NET.Client/Generated/Commands/<Sub>/*.g.cs`  | Request and response `readonly struct` types with `[JsonPropertyName]` attributes                                |
|                          | `src/FlipperZero.NET.Client/Generated/Streams/<Sub>/*.g.cs`   | Event `readonly struct` types                                                                                    |
|                          | `src/FlipperZero.NET.Client/Generated/Enums/*.g.cs`           | C# `enum` types                                                                                                  |
|                          | `src/FlipperZero.NET.Client/Generated/Extensions/*.g.cs`      | Extension methods on `FlipperRpcClient`                                                                          |
| `normalizer-codegen.csx` | `src/FlipperZero.NET.Client/Generated/RpcJsonNormalizer.g.cs` | Switch expressions mapping wire keys ↔ full names, integer enums ↔ names, for diagnostics output                 |

---

## Adding a new command — checklist

1. **Append** a new entry to `schema/command-registry.json`. The key must be the next integer after the current highest. Never reuse or skip IDs.

2. **Create** `schema/commands/<subsystem>/<cmd>.json` (or `schema/streams/<name>.json` for a stream). Use an existing schema as a template.

3. **Add a new enum** in `schema/enums/<Name>.json` if the command introduces a new constrained value set.

4. **Run codegen** (both scripts, then build) to verify the schema is valid and no compile errors are introduced.

5. **Write the C handler** in `src/FlipperZeroRpcDaemon/handlers/<subsystem>/<cmd>.{h,c}` and register it in the generated dispatch table. See `AGENTS.md` for handler conventions.

6. **Write C# overrides** if codegen is insufficient (`"csharp": { "skip": [...] }` in the schema + hand-written partial in `Commands/<Subsystem>/`).

7. **Bump `DAEMON_PROTOCOL_VERSION`** in `handlers/system/daemon_info.h` only for breaking wire-format changes.
