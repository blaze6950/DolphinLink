/**
 * rpc_handlers.h — RPC command handler declarations
 *
 * Each handler implements one RPC command.  Handlers are registered in the
 * command table defined in rpc_dispatch.c.
 *
 * Handler signature: void handler(uint32_t request_id, const char* json)
 *   - request_id  The id extracted from the incoming request.
 *   - json        The complete raw JSON request line (NUL-terminated).
 *
 * Handlers must be called from the main thread only.
 */

#pragma once

#include <stdint.h>

/* Simple request-response */
void ping_handler(uint32_t id, const char* json);

/* Streaming commands — each opens a hardware stream and returns a stream id */
void ir_receive_start_handler(uint32_t id, const char* json);
void gpio_watch_start_handler(uint32_t id, const char* json);
void subghz_rx_start_handler(uint32_t id, const char* json);
void nfc_scan_start_handler(uint32_t id, const char* json);

/* Stream lifecycle */
void stream_close_handler(uint32_t id, const char* json);
