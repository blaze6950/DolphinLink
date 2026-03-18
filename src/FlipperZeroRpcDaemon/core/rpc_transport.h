/**
 * rpc_transport.h — USB CDC transport layer
 *
 * Provides the outbound send function and the RX framing layer that
 * accumulates raw bytes from the CDC ISR into complete NDJSON lines,
 * then posts them to rx_queue for consumption on the main thread.
 *
 * Threading model:
 *   cdc_rx_callback() runs in USB interrupt context — only safe to call
 *   furi_hal_cdc_receive() and furi_message_queue_put() from there.
 *   cdc_send() must be called from the main thread only.
 */

#pragma once

#include <furi.h>
#include <stdint.h>

/* CDC interface number (1 = second device in usb_cdc_dual;
 * interface 0 is reserved for the system RPC used by qFlipper). */
#define RPC_CDC_IF 1

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

/** Send a NUL-terminated JSON string out over CDC interface 1.
 *  Must be called from the main thread only. */
void cdc_send(const char* json);

/** CDC RX callback — registered with furi_hal_cdc_set_callbacks().
 *  Runs in USB interrupt context; accumulates bytes and enqueues complete lines. */
void cdc_rx_callback(void* ctx);
