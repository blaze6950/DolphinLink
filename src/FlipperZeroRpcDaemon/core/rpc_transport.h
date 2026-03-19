/**
 * rpc_transport.h — USB CDC transport layer
 *
 * Provides the outbound TX pipeline and the RX framing layer.
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
 *     → rpc_dispatch()
 */

#pragma once

#include <furi.h>
#include <furi_hal_usb_cdc.h>
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

/** Send a NUL-terminated JSON string out over CDC interface 1.
 *  Safe to call from the main thread; returns as soon as all bytes are
 *  enqueued in the TX stream buffer (may block briefly if buffer is full). */
void cdc_send(const char* json);

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
