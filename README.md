# DolphinLink

[![CI](https://github.com/blaze6950/DolphinLink/actions/workflows/ci.yml/badge.svg)](https://github.com/blaze6950/DolphinLink/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/DolphinLink.Client)](https://www.nuget.org/packages/DolphinLink.Client)
[![NuGet](https://img.shields.io/nuget/v/DolphinLink.Bootstrapper)](https://www.nuget.org/packages/DolphinLink.Bootstrapper)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/en-us/)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue)](LICENSE)

**Control your Flipper Zero from C# — over USB, via a simple NDJSON RPC.**

```
[ Flipper Zero ]                    [ Your App ]
   C FAP daemon   ←— USB CDC 1 —→   .NET client
   (on-device)                       Bootstrapper auto-installs & launches the FAP
                                      Blazor WASM variant works straight from the browser
```

---

## Why?

The Flipper Zero is an incredibly capable piece of hardware — IR blaster, Sub-GHz radio, NFC reader, RFID reader, GPIO pins, iButton, a display, and more, all in one pocket device. But to make it do something custom, you need to write C. And C is unforgiving: manual memory management, no standard library to speak of, a steep learning curve, and a slow edit-flash-test loop that can turn a simple idea into a multi-day exercise.

**DolphinLink removes that barrier.** Instead of writing C firmware, you keep the Flipper running its stock firmware and talk to it from a host application using modern C# over USB. The daemon is already written — you just call methods.

This matters because:

- **C# developers can finally build real hardware projects** without touching embedded C. You get `async`/`await`, strong types, NuGet packages, full IDE support, and a debugger — with actual Flipper hardware responding on the other end.
- **The barrier to entry drops dramatically.** An idea that would take days to implement as a FAP can become an afternoon's work in C#.
- **It spreads what Flipper can do.** The more people can build with it, the more creative and unexpected the use cases become. DolphinLink is a bet that opening the hardware to a wider audience leads to things nobody has thought of yet.

### Real-world use cases

| Use case                      | What you do                                                                                                                                                                                   |
|-------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **Home automation / IoT**     | Use the Flipper as a programmable RF, IR, and NFC bridge — send raw Sub-GHz signals, replay IR remotes, scan NFC tags, all from your .NET app or Home Assistant integration.                  |
| **Security research tooling** | Script signal capture and replay workflows in C# with full control over timing, frequency, and raw payloads. Integrate with existing .NET analysis pipelines.                                 |
| **Hardware prototyping**      | Wire sensors to the GPIO pins and read them from C#. Use the ADC, control the 5 V rail, watch for pin changes — Flipper becomes a capable USB dev board you can script.                       |
| **Education**                 | Learn how IR, Sub-GHz, NFC, and RFID actually work by writing code that talks to real hardware, watching the raw NDJSON in the built-in RPC console, and reading the open wire protocol spec. |

---

## What it is

| Sub-project      | Language    | Role                                                                                                                                |
|------------------|-------------|-------------------------------------------------------------------------------------------------------------------------------------|
| **RPC Daemon**   | C (FAP)     | Runs on-device. Translates NDJSON commands into Flipper SDK calls. Uses USB CDC interface 1, leaving interface 0 free for qFlipper. |
| **RPC Client**   | C# / .NET 8 | Async, strongly-typed API for all 47 commands. Code-generated from JSON schemas.                                                    |
| **Bootstrapper** | C# / .NET 8 | Installs and launches the daemon FAP automatically via the Flipper's native protobuf RPC, then hands you a ready-to-use client.     |

A **Blazor WASM** app (`src/DolphinLink.Web`) talks to the Flipper directly from a Chromium browser over the Web Serial API — no drivers, no install.

---

## What it is NOT

- **Not affiliated with Flipper Devices Inc.** This is an independent hobbyist project.
- **Not a replacement for qFlipper.** It runs alongside it (on CDC interface 1); qFlipper keeps working normally.
- **Not a full Flipper API.** BLE, BadUSB, U2F, UART emulation, and app-protocol features are out of scope.
- **Not production-hardened.** Treat it as a solid foundation, not a battle-tested SDK.

---

## How it works

- A small C daemon (FAP) runs on the Flipper under stock firmware and exposes an NDJSON RPC over **USB CDC interface 1**.
- **CDC interface 0 stays free** for qFlipper — you can use both at the same time.
- The **Bootstrapper** uses the Flipper's native protobuf RPC to upload the daemon FAP to the SD card and launch it automatically — no manual steps.
- The **Blazor WASM** variant uses the browser's Web Serial API to connect directly from Chrome or Edge — nothing to install.
- The wire protocol is plain newline-delimited JSON, documented in full in [PROTOCOL.md](PROTOCOL.md) — easy to implement in any language.

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

> **[Try it live →](https://blaze6950.github.io/DolphinLink/)**
> Requires Chrome or Edge (Web Serial API). Connect your Flipper and everything runs in the browser — no install.

> **Reading this inside the web app?** Use the sidebar to jump directly to the Playground, Docs, and Demos pages.

The web app is both a demo and an interactive API reference:

- **Playground** — schema-driven browser of every command and stream. Fields are auto-generated with the right input types (enum dropdowns, hex inputs, file pickers). Fill in the fields, hit Send, and see the typed response — or start a stream and watch live events scroll in. The **RPC console** at the bottom of every page shows the raw NDJSON traffic with timestamps, round-trip times, and optional pretty-print.
- **Docs** — the full documentation rendered in-app: Architecture, Wire Protocol, Schema Reference, and Diagnostics.
- **Demos**
  - *LED color picker* — full HSV picker and RGB sliders; every change is sent to the Flipper's RGB LED in real time.
  - *Screen canvas* — draw lines, rectangles, and text on a 128×64 preview, then push it to the Flipper display.
  - *Flipper Gamepad* — play Snake using the Flipper's physical D-pad buttons, streamed live over USB.

---

## Programmatic Access — Quick Start

No code needed to explore the API — the Playground covers that. When you're ready to integrate the Flipper into your own C# application:

### 1. Install

```
dotnet add package DolphinLink.Bootstrapper
```

Or, if you only need the client without the auto-install flow:

```
dotnet add package DolphinLink.Client
```

### 2. Connect with the Bootstrapper (recommended)

The Bootstrapper uploads the daemon FAP if it isn't already on the SD card, launches it, and returns a connected client — all in one call. `COM3` is the system port (native RPC / qFlipper), `COM4` is the daemon port (opens after the FAP starts).

```csharp
using DolphinLink.Bootstrapper;

var result = await Bootstrapper.BootstrapAsync("COM3", "COM4");
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
3. Use the C# client in [`src/DolphinLink.Client/`](src/DolphinLink.Client/) as a reference implementation.
4. Ask an LLM to generate the host library. Point it at `PROTOCOL.md`, the schema files, and the C# source — it has everything it needs.

---

## Testing caveat

Not every command has been tested end-to-end on real hardware. If you run into a bug or unexpected behavior, please **[open an issue](https://github.com/blaze6950/DolphinLink/issues)** — reports are very welcome.

---

## Reference

|                          |                                      |
|--------------------------|--------------------------------------|
| Wire protocol            | [`PROTOCOL.md`](PROTOCOL.md)         |
| Architecture & threading | [`ARCHITECTURE.md`](ARCHITECTURE.md) |
| Schema format & codegen  | [`SCHEMA.md`](SCHEMA.md)             |
| Diagnostics              | [`DIAGNOSTICS.md`](DIAGNOSTICS.md)   |
| Command & enum schemas   | [`schema/`](schema/)                 |

---

## License

Licensed under the [Apache License 2.0](LICENSE).
