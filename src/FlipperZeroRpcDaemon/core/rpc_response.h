/**
 * rpc_response.h — RPC response formatting helpers (Wire Format V3)
 *
 * All daemon-to-host messages now use a compact envelope with single-letter
 * field names and a numeric type discriminator:
 *
 *   {"t":0,"i":<id>}                    ← void success
 *   {"t":0,"i":<id>,"p":<obj>}          ← success with data
 *   {"t":0,"i":<id>,"e":"<code>"}       ← error
 *
 * Handlers build ONLY the payload object (without wrapping braces) and pass it
 * to rpc_send_data_response().  The envelope is added by these helpers.
 *
 * All functions must be called from the main thread only.
 */

#pragma once

#include <stdint.h>
#include <inttypes.h>

/**
 * Send an error response and log it.
 *
 *   Wire:   {"t":0,"i":<id>,"e":"<error_code>"}\n
 *   Screen: #<id> <cmd_name> -> err:<error_code>   (truncated to fit)
 *
 * @param id          Request ID from the incoming JSON.
 * @param error_code  Error code string (e.g. "resource_busy").
 * @param cmd_name    Command name for the log entry (e.g. "ble_scan_start").
 */
void rpc_send_error(uint32_t id, const char* error_code, const char* cmd_name);

/**
 * Send a simple success response (no data payload) and log it.
 *
 *   Wire:   {"t":0,"i":<id>}\n
 *   Screen: #<id> <cmd_name> -> ok
 *
 * @param id        Request ID.
 * @param cmd_name  Command name for the log entry.
 */
void rpc_send_ok(uint32_t id, const char* cmd_name);

/**
 * Send a success response with a data payload and log it.
 *
 *   Wire:   {"t":0,"i":<id>,"p":<payload_json>}\n
 *   Screen: <log_entry>
 *
 * Use this when the success response contains a custom data payload that
 * rpc_send_ok() cannot express (e.g. ping with pong data, gpio_read with
 * a value).  The caller builds only the payload JSON object (the complete
 * {...} object, not a field fragment) and passes it as payload_json.
 *
 * For large payloads that cannot fit in a stack buffer, allocate the
 * payload_json on the heap and free it after this call returns.
 *
 * @param id           Request ID.
 * @param payload_json Complete JSON object string for the "p" field,
 *                     e.g. "{\"pong\":true}" or "{\"level\":false}".
 * @param log_entry    Short string to display in the on-screen log.
 */
void rpc_send_data_response(uint32_t id, const char* payload_json, const char* log_entry);
