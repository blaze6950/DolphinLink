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

void ping_handler(uint32_t id, const char* json);
void ble_scan_start_handler(uint32_t id, const char* json);
void stream_close_handler(uint32_t id, const char* json);
