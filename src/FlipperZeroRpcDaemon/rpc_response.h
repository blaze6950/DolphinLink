/**
 * rpc_response.h — RPC response formatting helpers
 *
 * Eliminates the repeated pattern of:
 *   snprintf(buf) → cdc_send(buf) → snprintf(log) → cmd_log_push(log)
 * that appears throughout the dispatcher and all command handlers.
 *
 * All functions must be called from the main thread only.
 */

#pragma once

#include <stdint.h>
#include <inttypes.h>

/**
 * Send an error response and log it.
 *
 *   Wire:   {"id":<id>,"error":"<error_code>"}\n
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
 *   Wire:   {"id":<id>,"status":"ok"}\n
 *   Screen: #<id> <cmd_name> -> ok
 *
 * @param id        Request ID.
 * @param cmd_name  Command name for the log entry.
 */
void rpc_send_ok(uint32_t id, const char* cmd_name);

/**
 * Send an arbitrary pre-formatted JSON response and log it.
 *
 * Use this when the success response contains a custom data payload that
 * rpc_send_ok() cannot express (e.g. the stream-opened response).
 *
 * @param json_line  Complete '\n'-terminated JSON line to send over CDC.
 * @param log_entry  Short string to display in the on-screen log.
 */
void rpc_send_response(const char* json_line, const char* log_entry);
