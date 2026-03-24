# FlipperZero.NET

[![CI](https://github.com/YOUR_ORG/FlipperZero.NET/actions/workflows/ci.yml/badge.svg)](https://github.com/YOUR_ORG/FlipperZero.NET/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/FlipperZero.NET.Client)](https://www.nuget.org/packages/FlipperZero.NET.Client)
[![NuGet](https://img.shields.io/nuget/v/FlipperZero.NET.Bootstrapper)](https://www.nuget.org/packages/FlipperZero.NET.Bootstrapper)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/en-us/)

**Control your Flipper Zero from .NET (C#) — (or any language) — over USB, via a simple NDJSON RPC.**

```
[ Flipper Zero ]                    [ Your App ]
   C FAP daemon   ←— USB CDC 1 —→   .NET client
   (on-device)                       Bootstrapper auto-installs & launches the FAP
                                      Blazor WASM variant works straight from the browser
```

---

## What it is

| Sub-project      | Language    | Role                                                                                                                                |
|------------------|-------------|-------------------------------------------------------------------------------------------------------------------------------------|
| **RPC Daemon**   | C (FAP)     | Runs on-device. Translates NDJSON commands into Flipper SDK calls. Uses USB CDC interface 1, leaving interface 0 free for qFlipper. |
| **RPC Client**   | C# / .NET 8 | Async, strongly-typed API for all 46 commands. Code-generated from JSON schemas.                                                    |
| **Bootstrapper** | C# / .NET 8 | Installs and launches the daemon FAP automatically via the Flipper's native protobuf RPC, then hands you a ready-to-use client.     |

A **Blazor WASM** sample (`samples/FlipperZero.Web`) talks to the Flipper directly from a Chromium browser over the Web Serial API — no drivers, no install.

---

## What it is NOT

- **Not affiliated with Flipper Devices Inc.** This is an independent hobbyist project.
- **Not a replacement for qFlipper.** It runs alongside it (on CDC interface 1); qFlipper keeps working normally.
- **Not a full Flipper API.** BLE, BadUSB, U2F, UART emulation, and app-protocol features are out of scope.
- **Not production-hardened.** Treat it as a solid foundation, not a battle-tested SDK.

---

## Supported subsystems

| Subsystem     | Commands                                                 | Streams          |
|---------------|----------------------------------------------------------|------------------|
| System        | device info, power, RTC, region, reboot, frequency check | —                |
| GPIO          | read, write, ADC, 5 V rail                               | pin change watch |
| IR            | TX decoded, TX raw                                       | receive          |
| Sub-GHz       | TX, RSSI                                                 | RX               |
| NFC           | —                                                        | tag scan         |
| RFID (LF)     | —                                                        | card read        |
| iButton       | —                                                        | key read         |
| Storage       | info, list, read, write, mkdir, remove, stat             | —                |
| Notifications | LED, RGB LED, vibro, speaker, backlight                  | —                |
| UI / Screen   | draw text / rect / line, flush, acquire, release         | —                |
| Input         | —                                                        | button events    |

---

## Interactive docs & playground

> **[Try it live →](https://YOUR_ORG.github.io/FlipperZero.NET/)**  
> Requires Chrome or Edge (Web Serial API). Connect your Flipper and everything runs in the browser — no install.

A Blazor WASM app that serves as both a demo and an interactive API reference:

- **Home** — live daemon & device info, quick LED blink buttons.
- **Playground** — schema-driven browser of every command and stream. Fill in fields, fire commands live, watch raw NDJSON flow in the built-in RPC console. Effectively the best API reference you can get.
- **Demos**
  - *LED color picker* — full HSV picker; every change is sent to the Flipper's RGB LED in real time.
  - *Screen canvas* — draw lines, rectangles, and text on a 128×64 preview, then push it to the Flipper display.
  - *Snake / Gamepad* — play Snake using the Flipper's physical D-pad buttons, streamed live over USB.

---

## Quick start

### 1. Install

```
dotnet add package FlipperZero.NET.Bootstrapper
```

Or, if you just need the client without the auto-install flow:

```
dotnet add package FlipperZero.NET.Client
```

### 2. Connect with the Bootstrapper (recommended)

The Bootstrapper uploads the daemon FAP if it isn't already on the SD card, launches it, and returns a connected client — all in one call. `COM3` is the system port (native RPC / qFlipper), `COM4` is the daemon port (opens after the FAP starts).

```csharp
using FlipperZero.NET.Bootstrapper;

var result = await FlipperBootstrapper.BootstrapAsync("COM3", "COM4");
await using var flipper = result.Client;
```

### 3. Call commands

```csharp
// Read a GPIO pin
bool level = await flipper.GpioReadAsync(GpioPin.Pin1);
Console.WriteLine($"Pin 1: {level}");

// Flash the RGB LED green
await flipper.LedSetRgbAsync(0, 255, 0);
await Task.Delay(400);
await flipper.LedSetRgbAsync(0, 0, 0);

// Stream IR receive events
await foreach (var e in flipper.IrReceiveStartAsync())
{
    Console.WriteLine($"IR: {e.Protocol} addr={e.Address} cmd={e.Command}");
}
```

---

## Bring your own language

The wire protocol is plain **NDJSON over USB CDC** — one JSON object per line. If you want a client in Python, Java, Go, Rust, or anything else:

1. Read [`PROTOCOL.md`](PROTOCOL.md) — the full wire format fits on one page.
2. Browse [`schema/`](schema/) — every command, field type, and enum is machine-readable JSON.
3. Use the C# client in [`src/FlipperZero.NET.Client/`](src/FlipperZero.NET.Client/) as a reference implementation.
4. Ask an LLM to generate the host library. Point it at `PROTOCOL.md`, the schema files, and the C# source — it has everything it needs.

---

## Testing caveat

Not every command has been tested end-to-end on real hardware. If you run into a bug or unexpected behavior, please **[open an issue](../../issues)** — reports are very welcome.

---

## Reference

|                          |                                      |
|--------------------------|--------------------------------------|
| Wire protocol            | [`PROTOCOL.md`](PROTOCOL.md)         |
| Architecture & threading | [`ARCHITECTURE.md`](ARCHITECTURE.md) |
| Schema format & codegen  | [`SCHEMA.md`](SCHEMA.md)             |
| Command & enum schemas   | [`schema/`](schema/)                 |
