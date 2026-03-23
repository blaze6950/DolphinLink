// webserial-interop.js
// WebSerial interop module for FlipperZero.NET.WebSerial.
//
// This module is loaded as an ES module by the .NET JSImport bridge.
// It manages WebSerial port handles and drives the ReadableStream pump
// that delivers received bytes to the .NET layer.
//
// Port handle lifecycle:
//   openPort(vendorId, productId, baudRate) → portId (integer, or -1)
//   startReading(portId)                    → starts the pump loop (fire-and-forget)
//   write(portId, data)                     → Uint8Array write
//   setSignals(portId, dtr)                 → DTR modem line control
//   closePort(portId)                       → closes the port, keeps permission grant
//   forgetPort(portId)                      → closes the port AND revokes the permission grant
//
// Data delivery (JS → .NET):
//   The JS pump calls the [JSExport] method
//   FlipperZero.NET.WebSerial.WebSerialInterop.OnData(portId, Uint8Array)
//   via the .NET runtime's assembly exports.
//   An EMPTY Uint8Array (length 0) signals EOF/error.
//   The .NET side dispatches to the correct WebSerialStream via a per-port table.

// -------------------------------------------------------------------------
// .NET runtime exports (resolved at module initialisation)
// -------------------------------------------------------------------------

// Resolved once in initModule(); used by the read pump.
let _onDataExport = null;

/**
 * Called by WebSerialPort.CreateAsync via JSHost.ImportAsync.
 * Resolves the [JSExport] OnData method from the .NET assembly so the pump
 * can call it without re-resolving on every chunk.
 *
 * @param {string} assemblyName - The DLL name, e.g. "FlipperZero.NET.WebSerial.dll"
 * @returns {Promise<void>}
 */
export async function initModule(assemblyName) {
    const { getAssemblyExports } = await globalThis.getDotnetRuntime(0);
    const exports = await getAssemblyExports(assemblyName);
    _onDataExport =
        exports['FlipperZero']['NET']['WebSerial']['WebSerialInterop']['OnData'];
}

// -------------------------------------------------------------------------
// Port handle registry
// -------------------------------------------------------------------------

let _nextPortId = 1;

/**
 * @type {Map<number, {
 *   port: SerialPort,
 *   reader: ReadableStreamDefaultReader<Uint8Array> | null,
 *   writer: WritableStreamDefaultWriter<Uint8Array>,
 * }>}
 */
const _ports = new Map();

/**
 * Ports that have been closed by closePort() but not yet forgotten.
 * Keyed by portId; value is the raw SerialPort object.
 *
 * forgetPort() checks this map as a fallback when the portId is no
 * longer in _ports (i.e. the bootstrapper already called closePort()
 * via DisposeAsync before onBeforeDaemonConnect called forgetPort()).
 * This lets forget() still revoke the permission grant even after the
 * port handle has been removed from the active _ports registry.
 *
 * Entries are evicted when forgetPort() is called or when the map grows
 * beyond _CLOSED_PORTS_MAX, at which point the oldest entries are dropped
 * to prevent unbounded memory growth in long-running sessions.
 *
 * @type {Map<number, SerialPort>}
 */
const _closedPorts = new Map();

/** Maximum number of entries retained in _closedPorts before eviction. */
const _CLOSED_PORTS_MAX = 32;

// -------------------------------------------------------------------------
// Public API (called via [JSImport] from .NET)
// -------------------------------------------------------------------------

/**
 * Returns true if the WebSerial API is available in this browser.
 * @returns {boolean}
 */
export function isSupported() {
    return typeof navigator !== 'undefined' && 'serial' in navigator;
}

/**
 * Opens a serial port by showing the browser's port picker.
 *
 * @param {number} usbVendorId  - USB vendor ID filter (0 = no filter)
 * @param {number} usbProductId - USB product ID filter (0 = no filter)
 * @param {number} baudRate     - Baud rate (e.g. 115200)
 * @returns {Promise<number>}   - Port handle ID, or -1 on failure/cancel
 */
export async function openPort(usbVendorId, usbProductId, baudRate) {
    let port;
    try {
        const filters = (usbVendorId !== 0)
            ? [{ usbVendorId, ...(usbProductId !== 0 ? { usbProductId } : {}) }]
            : [];
        port = await navigator.serial.requestPort({ filters });
    } catch (e) {
        // User cancelled the picker or permission denied.
        console.warn('[webserial] openPort: requestPort cancelled or denied:', e);
        return -1;
    }

    try {
        await port.open({ baudRate });
    } catch (e) {
        console.error('[webserial] openPort: port.open() failed:', e);
        return -1;
    }

    const portId = _nextPortId++;
    console.log(`[webserial] openPort: portId=${portId} baudRate=${baudRate}`);

    // Acquire the writer once; keep it for the lifetime of this handle.
    const writer = port.writable.getWriter();

    _ports.set(portId, {
        port,
        reader: null,   // created lazily in startReading
        writer,
    });

    return portId;
}

/**
 * Starts the ReadableStream pump for the given port.
 *
 * For each received chunk, calls the [JSExport] .NET method
 * WebSerialInterop.OnData(portId, Uint8Array).  When the stream ends,
 * calls OnData(portId, new Uint8Array(0)) as an EOF sentinel.
 *
 * This function returns immediately; the pump runs asynchronously.
 *
 * @param {number} portId - Handle from openPort
 */
export function startReading(portId) {
    const handle = _ports.get(portId);
    if (!handle) throw new Error(`Unknown portId: ${portId}`);

    const reader = handle.port.readable.getReader();
    handle.reader = reader;

    console.log(`[webserial] startReading: portId=${portId} — pump started`);

    // Run the pump as a fire-and-forget async function.
    (async () => {
        let chunkCount = 0;
        let byteCount = 0;
        try {
            while (true) {
                const { value, done } = await reader.read();
                if (done) {
                    console.log(`[webserial] pump portId=${portId}: done=true (clean EOF) after ${chunkCount} chunks / ${byteCount} bytes`);
                    break;
                }
                if (value && value.byteLength > 0) {
                    chunkCount++;
                    byteCount += value.byteLength;
                    console.log(`[webserial] pump portId=${portId}: chunk #${chunkCount} ${value.byteLength}B (total ${byteCount}B)`);
                    _onDataExport(portId, value);
                }
            }
        } catch (e) {
            // Port closed, disconnected, or aborted — signal EOF to .NET.
            console.error(`[webserial] pump portId=${portId}: exception after ${chunkCount} chunks / ${byteCount} bytes:`, e);
        } finally {
            console.log(`[webserial] pump portId=${portId}: finally — sending EOF sentinel`);
            try { reader.releaseLock(); } catch (_) {}
            // Empty array = EOF sentinel (null is not a valid [JSImport] byte[] value).
            _onDataExport(portId, new Uint8Array(0));
        }
    })();
}

/**
 * Writes data to the given port.
 *
 * @param {number}     portId - Handle from openPort
 * @param {Uint8Array} data   - Bytes to write
 * @returns {Promise<void>}
 */
export async function write(portId, data) {
    const handle = _ports.get(portId);
    if (!handle) throw new Error(`Unknown portId: ${portId}`);
    console.log(`[webserial] write portId=${portId}: ${data.byteLength}B`);
    await handle.writer.write(data);
}

/**
 * Sets the DTR (Data Terminal Ready) modem control signal.
 *
 * @param {number}  portId  - Handle from openPort
 * @param {boolean} enabled - true = assert DTR, false = de-assert
 * @returns {Promise<void>}
 */
export async function setSignals(portId, enabled) {
    const handle = _ports.get(portId);
    if (!handle) throw new Error(`Unknown portId: ${portId}`);
    await handle.port.setSignals({ dataTerminalReady: enabled });
}

/**
 * Closes the port and releases OS-level resources, but keeps the browser's
 * permission grant so the port still appears in navigator.serial.getPorts().
 * Safe to call multiple times.
 *
 * The raw SerialPort reference is saved in _closedPorts so that a subsequent
 * call to forgetPort() can still revoke the permission grant even after this
 * function has removed the port from the active _ports registry.
 *
 * @param {number} portId - Handle from openPort
 * @returns {Promise<void>}
 */
export async function closePort(portId) {
    const handle = _ports.get(portId);
    if (!handle) return; // already closed

    console.log(`[webserial] closePort: portId=${portId}`);
    _ports.delete(portId);

    // Cancel the reader and await it so the ReadableStream lock is released before
    // port.close() is called.  Without await, port.close() races the pump's finally
    // block and may throw "The port is locked" on some browser versions.
    if (handle.reader) {
        try { await handle.reader.cancel(); } catch (_) {}
    }
    try { handle.writer.releaseLock(); } catch (_) {}
    try { await handle.port.close(); } catch (_) {}

    // Preserve the raw SerialPort reference so forgetPort() can revoke the
    // permission grant even if closePort() was called first (e.g. by the
    // bootstrapper's DisposeAsync before onBeforeDaemonConnect fires).
    // Evict oldest entries if the map has grown beyond the cap.
    if (_closedPorts.size >= _CLOSED_PORTS_MAX) {
        const oldestKey = _closedPorts.keys().next().value;
        _closedPorts.delete(oldestKey);
        console.warn(`[webserial] closePort: _closedPorts cap reached — evicted portId=${oldestKey}`);
    }
    _closedPorts.set(portId, handle.port);
}

/**
 * Revokes the browser's permission grant for the given port, completely
 * removing it from navigator.serial.getPorts() and releasing all OS-level
 * claims.
 *
 * If the port is still active (in _ports), it is closed and forgotten.
 * If the port was already closed by closePort() (in _closedPorts), the
 * saved SerialPort reference is used to still call forget() — this handles
 * the case where the bootstrapper's DisposeAsync ran before this call.
 *
 * SerialPort.forget() was added in Chrome 103.  On older browsers only
 * close() is called; the permission grant is not revoked.
 *
 * Note: forget() closes the port itself, so close() must NOT be called
 * before forget() — doing so can leave the port in a state where forget()
 * silently fails.
 *
 * @param {number} portId - Handle from openPort
 * @returns {Promise<void>}
 */
export async function forgetPort(portId) {
    let port;

    const handle = _ports.get(portId);
    if (handle) {
        // Port is still active — cancel reader (awaited) and release writer before forgetting.
        console.log(`[webserial] forgetPort: portId=${portId} (active)`);
        _ports.delete(portId);
        if (handle.reader) {
            try { await handle.reader.cancel(); } catch (_) {}
        }
        try { handle.writer.releaseLock(); } catch (_) {}
        port = handle.port;
    } else {
        // Port was already closed by closePort(); use the preserved reference.
        port = _closedPorts.get(portId);
        if (!port) {
            console.warn(`[webserial] forgetPort: portId=${portId} — not found in _ports or _closedPorts`);
            return; // already forgotten or never opened
        }
        console.log(`[webserial] forgetPort: portId=${portId} (from _closedPorts fallback)`);
    }

    // Always remove from _closedPorts regardless of which branch we took.
    _closedPorts.delete(portId);

    if (typeof port.forget === 'function') {
        // forget() both closes the port AND revokes the permission grant.
        // Do NOT call close() before forget() — it can cause forget() to no-op.
        try {
            await port.forget();
            console.log(`[webserial] forgetPort: portId=${portId} — port.forget() succeeded`);
        } catch (e) {
            console.error(`[webserial] forgetPort: portId=${portId} — port.forget() threw:`, e);
        }
    } else {
        // Chrome < 103 fallback: close only (permission grant is not revoked).
        console.warn(`[webserial] forgetPort: portId=${portId} — port.forget() not available, falling back to close()`);
        try { await port.close(); } catch (_) {}
    }
}

/**
 * Returns a JSON-encoded array of port IDs for all previously-granted SerialPorts
 * that match the given USB VID/PID filter.  Does NOT require a user gesture.
 *
 * Returns a JSON string (e.g. "[1,2]") rather than a JS array because the
 * .NET JSImport source generator does not support Task<int[]> as a return type.
 *
 * The returned IDs are ephemeral handle numbers backed by entries in _ports —
 * the caller is responsible for closing each one with closePort() when done
 * (or when a port cannot be opened / is the wrong interface).
 *
 * @param {number} usbVendorId  - USB vendor ID filter (0 = no filter)
 * @param {number} usbProductId - USB product ID filter (0 = no filter)
 * @param {number} baudRate     - Baud rate used when opening each port
 * @returns {Promise<string>} JSON array of port handle IDs (e.g. "[]" or "[1,2]")
 */
export async function getPorts(usbVendorId, usbProductId, baudRate) {
    const allPorts = await navigator.serial.getPorts();
    const result = [];

    for (const port of allPorts) {
        const info = port.getInfo();

        // Apply optional VID/PID filter.
        if (usbVendorId !== 0 && info.usbVendorId !== usbVendorId) continue;
        if (usbProductId !== 0 && info.usbProductId !== usbProductId) continue;

        // Skip ports that are already open and tracked (already in _ports map).
        let alreadyTracked = false;
        for (const [, h] of _ports) {
            if (h.port === port) { alreadyTracked = true; break; }
        }
        if (alreadyTracked) continue;

        // Try to open the port; skip it if it is already in use by another app.
        try {
            await port.open({ baudRate });
        } catch (_) {
            continue;
        }

        const portId = _nextPortId++;
        const writer = port.writable.getWriter();
        _ports.set(portId, { port, reader: null, writer });
        result.push(portId);
    }

    return JSON.stringify(result);
}
