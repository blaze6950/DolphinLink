/**
 * rpc_transport.h — USB CDC transport layer
 *
 * Provides the outbound TX pipeline, the RX framing layer, and the
 * bidirectional heartbeat / keep-alive subsystem.
 *
 * TX pipeline (non-blocking for callers on the main thread):
 *   cdc_send(json)
 *     → furi_stream_buffer_send() into tx_stream   [blocks only if buffer full]
 *   TX thread "RpcTx" (dedicated FuriThread, 512 B stack)
 *     → drains tx_stream in ≤64-byte chunks
 *     → paces each chunk with tx_semaphore (released by tx_ep_callback)
 *     → handles ZLP when last chunk was exactly 64 bytes
 *   cdc_tx_callback() [USB ISR]
 *     → furi_semaphore_release(tx_semaphore)
 *
 * RX pipeline (USB ISR → main thread):
 *   cdc_rx_callback() [USB ISR]
 *     → furi_hal_cdc_receive() → byte accumulation
 *     → on '\n': furi_message_queue_put(rx_queue, ...)
 *   on_rx_queue() [FuriEventLoop, main thread]
 *     → skips empty keep-alive frames (len <= 1)
 *     → rpc_dispatch()
 *
 * Heartbeat subsystem (runs on the main thread via FuriEventLoopTimer):
 *
 *   Architecture
 *   ============
 *   Transport layer is the correct home for keep-alive logic: it has
 *   knowledge of when bytes are sent and received, but no knowledge of
 *   what is inside packets.  The RPC dispatch layer above it is not involved.
 *
 *   Bidirectional design:
 *     - TX watchdog: if no message has been sent for ≥ heartbeat_tx_idle_ms,
 *       emit a bare '\n' keep-alive frame.
 *     - RX watchdog: if no message has been received for ≥ heartbeat_rx_timeout_ms,
 *       the host is considered gone.  Tears down all streams and resources.
 *
 *   Keep-alive payload: a bare '\n' (minimum NDJSON frame, 1 byte on the wire).
 *   The host's HeartbeatTransport layer intercepts empty lines before they reach
 *   the RPC client, and sends its own '\n' keep-alives in the same fashion.
 *   Neither side dispatches heartbeat frames as RPC commands.
 *
 *   API for the entry point (flipper_zero_rpc_daemon.c):
 *     heartbeat_timer_alloc()  — allocate timer after event loop is created
 *     heartbeat_timer_start()  — call when host connects (DTR asserted)
 *     heartbeat_timer_stop()   — call when host disconnects (DTR released)
 *     heartbeat_timer_free()   — call before event loop is freed
 */

#pragma once

#include <furi.h>
#include <furi_hal_usb_cdc.h>
#include <gui/view_port.h>
#include <stdint.h>

/* CDC interface number (1 = second device in usb_cdc_dual;
 * interface 0 is reserved for the system RPC used by qFlipper). */
#define RPC_CDC_IF 1

/* Maximum USB CDC bulk-endpoint packet size (bytes).
 * USB full-speed CDC uses 64-byte bulk endpoints.  furi_hal_cdc_send() must
 * be called with payloads no larger than this value per call. */
#define CDC_DATA_SZ 64

/* Maximum bytes in one NDJSON line (including the trailing '\n').
 * 1024 bytes accommodates base64-encoded storage payloads (~700 bytes of
 * binary data) and raw IR/SubGHz timing arrays. */
#define RX_LINE_MAX 1024

/** A complete '\n'-terminated JSON line, sized for fixed-size queue elements. */
typedef struct {
    char data[RX_LINE_MAX];
    uint16_t len;
} RxLine;

/**
 * Message queue filled by cdc_rx_callback() and drained by the event loop.
 * Storage provided by flipper_zero_rpc_daemon.c.
 */
extern FuriMessageQueue* rx_queue;

/**
 * Allocate and start the TX thread, stream buffer, and semaphore.
 * Must be called after furi_hal_usb_set_config() and before
 * furi_hal_cdc_set_callbacks() so that the TX endpoint is ready.
 */
void cdc_transport_alloc(void);

/**
 * Stop the TX thread and free all TX resources.
 * Must be called after furi_hal_cdc_set_callbacks(RPC_CDC_IF, NULL, NULL)
 * so that no further tx_ep_callback firings can occur.
 */
void cdc_transport_free(void);

/**
 * Flipper tick count recorded immediately after the last byte of a message
 * was pushed into the TX stream buffer.  Updated by cdc_send() on every call.
 * Read by the heartbeat timer callback (main thread) to decide whether a
 * keepalive is needed.  Initialised to 0; set to furi_get_tick() on first send.
 */
extern uint32_t last_tx_ticks;

/**
 * Tick count of the last line received from the host.
 * Updated in on_rx_queue() on every dispatch call (after empty-line filter).
 * Read by the heartbeat timer callback on the same thread — no synchronisation
 * needed.  Initialised to 0; set to furi_get_tick() on the first received line.
 */
extern uint32_t last_rx_ticks;

/** Send a NUL-terminated string out over CDC interface 1.
 *  Safe to call from the main thread; returns as soon as all bytes are
 *  enqueued in the TX stream buffer (may block briefly if buffer is full).
 *  Updates last_tx_ticks after enqueuing. */
void cdc_send(const char* data);

/** CDC TX endpoint callback — registered in CdcCallbacks.tx_ep_callback.
 *  Runs in USB interrupt context; releases tx_semaphore so the TX thread
 *  can send the next chunk. */
void cdc_tx_callback(void* ctx);

/** CDC RX callback — registered with furi_hal_cdc_set_callbacks().
 *  Runs in USB interrupt context; accumulates bytes and enqueues complete lines. */
void cdc_rx_callback(void* ctx);

/* -------------------------------------------------------------------------
 * CDC control-line (DTR/RTS) change notification
 * ------------------------------------------------------------------------- */

/**
 * One CDC control-line state change event, posted from the USB ISR to the
 * main thread via cdc_ctrl_queue.
 */
typedef struct {
    uint8_t ctrl_lines; /**< Bitmask: bit 0 = DTR (CdcCtrlLineDTR), bit 1 = RTS */
} CdcCtrlEvent;

/**
 * Message queue filled by cdc_ctrl_line_callback() and drained by the
 * event loop on the main thread.
 * Storage provided by flipper_zero_rpc_daemon.c.
 */
extern FuriMessageQueue* cdc_ctrl_queue;

/** CDC control-line callback — registered in CdcCallbacks.ctrl_line_callback.
 *  Runs in USB interrupt context; posts a CdcCtrlEvent to cdc_ctrl_queue.
 *  context is the cdc_ctrl_queue pointer (passed via furi_hal_cdc_set_callbacks).
 *  Signature matches CdcCallbacks.ctrl_line_callback: void(*)(void*, CdcCtrlLine). */
void cdc_ctrl_line_callback(void* context, CdcCtrlLine ctrl_lines);

/* -------------------------------------------------------------------------
 * Heartbeat / keep-alive timer API
 *
 * The heartbeat timer is a transport-layer concern: it knows when bytes were
 * last sent and received, but has no knowledge of RPC commands or stream state.
 *
 * Usage in the entry point:
 *
 *   // After event loop is allocated:
 *   FuriEventLoopTimer* hb = heartbeat_timer_alloc(event_loop, view_port);
 *
 *   // When DTR is asserted (host connects):
 *   last_rx_ticks = furi_get_tick();   // seed RX watchdog
 *   heartbeat_timer_start(hb);
 *
 *   // When DTR drops (host disconnects):
 *   heartbeat_timer_stop(hb);
 *
 *   // Before event loop is freed:
 *   heartbeat_timer_free(hb);
 * ------------------------------------------------------------------------- */

/**
 * Allocate a periodic heartbeat timer tied to the given event loop.
 *
 * @param loop      The application's FuriEventLoop (must already be allocated).
 * @param view_port ViewPort used to trigger a GUI redraw when the host is
 *                  declared gone.  May be NULL if no GUI is used.
 * @return          Allocated timer; must be freed with heartbeat_timer_free().
 */
FuriEventLoopTimer* heartbeat_timer_alloc(FuriEventLoop* loop, ViewPort* view_port);

/**
 * Start the heartbeat timer.  Call this when the host connects (DTR asserted).
 * Seed last_rx_ticks with furi_get_tick() before calling so the RX watchdog
 * measures silence from the connection moment, not from the previous session.
 */
void heartbeat_timer_start(FuriEventLoopTimer* timer);

/**
 * Stop the heartbeat timer.  Call this when the host disconnects (DTR released).
 * Does not free the timer — it can be restarted on the next DTR assert.
 */
void heartbeat_timer_stop(FuriEventLoopTimer* timer);

/**
 * Free the heartbeat timer.  Must be called before the event loop is freed.
 */
void heartbeat_timer_free(FuriEventLoopTimer* timer);

/* -------------------------------------------------------------------------
 * Runtime-configurable heartbeat timing
 *
 * Both values are initialised to the compile-time defaults and may be
 * updated atomically on the main thread by calling heartbeat_apply_config().
 * They are reset to the defaults by heartbeat_reset_config() whenever the
 * host disconnects, so that each new session starts clean.
 * ------------------------------------------------------------------------- */

/**
 * TX idle interval (ms): a keep-alive '\n' is sent when no outbound
 * message has been transmitted for this many milliseconds.
 * Default: 3000.  Minimum enforced: 500.
 */
extern uint32_t heartbeat_tx_idle_ms;

/**
 * RX silence timeout (ms): the host is declared gone when no inbound
 * data has arrived for this many milliseconds.
 * Default: 10000.  Must be strictly > heartbeat_tx_idle_ms.
 */
extern uint32_t heartbeat_rx_timeout_ms;

/**
 * Apply new heartbeat timing values.
 *
 * Validation rules (enforced; call returns false on violation):
 *   - hb_ms  >= 500
 *   - to_ms  >= 2000
 *   - to_ms  >  hb_ms
 *
 * On success stores the values in heartbeat_tx_idle_ms / heartbeat_rx_timeout_ms
 * and returns true.  On failure leaves existing values unchanged and returns false.
 *
 * Must only be called from the main (event-loop) thread.
 *
 * @param hb_ms  Desired TX idle interval in milliseconds.
 * @param to_ms  Desired RX timeout in milliseconds.
 * @return       true if the values were accepted; false if validation failed.
 */
bool heartbeat_apply_config(uint32_t hb_ms, uint32_t to_ms);

/**
 * Reset heartbeat timing to compile-time defaults.
 * Called on every host disconnect so the next session starts clean.
 * Must only be called from the main (event-loop) thread.
 */
void heartbeat_reset_config(void);

/* -------------------------------------------------------------------------
 * LED connection indicator
 *
 * When the host sends a "configure" command with a "led" object, the daemon
 * stores the requested RGB colour and uses it as a connection indicator:
 *   - LED on  (stored colour) while a host is connected
 *   - LED off (all channels 0) when no host is connected
 *
 * The indicator is scoped to a single connection lifecycle: the config is
 * cleared (and the LED turned off) by heartbeat_reset_config() on every
 * disconnect, so the next session starts with the LED off until the host
 * re-sends its "configure" command.
 *
 * led_indicator_apply() is the single call-site for all LED state changes.
 * Pass connected=true to turn the LED on (only if enabled), false to turn
 * it off unconditionally.
 * ------------------------------------------------------------------------- */

/** True when the host has configured an LED indicator colour. */
extern bool led_indicator_enabled;

/** Effective red channel (0–255) for the connection indicator. */
extern uint8_t led_indicator_r;

/** Effective green channel (0–255) for the connection indicator. */
extern uint8_t led_indicator_g;

/** Effective blue channel (0–255) for the connection indicator. */
extern uint8_t led_indicator_b;

/**
 * Apply the LED indicator state.
 *
 * If connected is true and led_indicator_enabled is true, sets the RGB LED
 * to (led_indicator_r, led_indicator_g, led_indicator_b).
 * Otherwise turns all LED channels off (0, 0, 0).
 *
 * Must only be called from the main (event-loop) thread.
 */
void led_indicator_apply(bool connected);

/* -------------------------------------------------------------------------
 * Per-request timing metrics
 *
 * When metrics_enabled is true the dispatcher fills g_metrics with
 * furi_get_tick() timestamps for each processing phase, and the response
 * helpers append a "_m" object to every "t":0 response envelope.
 *
 * The flag is reset to false by heartbeat_reset_config() on every disconnect
 * so that each new session starts with metrics disabled (opt-in per session).
 *
 * Both globals are only read/written on the main (event-loop) thread.
 * Storage is in rpc_transport.c alongside the other config globals.
 * Declarations are in rpc_metrics.h (included below).
 * ------------------------------------------------------------------------- */

#include "rpc_metrics.h"
