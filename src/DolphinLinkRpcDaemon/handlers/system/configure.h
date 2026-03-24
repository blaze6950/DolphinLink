/**
 * configure.h — configure command handler declaration
 *
 * Propagates host-side configuration to the daemon during session startup.
 * The client sends this command immediately after daemon_info so the daemon
 * can align its behaviour (heartbeat timing, LED indicator, diagnostics) with
 * the host's expectations.
 *
 * Wire protocol:
 *   Request:  {"c":2,"i":N[,"hb":<u32>][,"to":<u32>][,"led":{"r":<u8>,"g":<u8>,"b":<u8>}][,"dx":<bool>]}
 *
 *   All arguments are optional.  If a field is absent the daemon retains its
 *   current value (initially the compile-time default).
 *
 *   Response (success):
 *     {"t":0,"i":N,"p":{"hb":<u32>,"to":<u32>[,"led":{"r":<u8>,"g":<u8>,"b":<u8>}],"dx":<bool>}}
 *   The response carries the *effective* values the daemon is now using.
 *   The "led" object is included in the response only when an LED indicator
 *   colour has been configured (i.e. when the request included a "led" field).
 *   "dx" is always echoed in the response.
 *
 *   Errors:
 *     invalid_config — the supplied heartbeat values failed validation:
 *                      heartbeat_ms < 500, timeout_ms < 2000,
 *                      or timeout_ms <= heartbeat_ms.
 *                      No values are changed on error.
 *
 * LED indicator behaviour:
 *   When "led" is present, the daemon stores the RGB colour and uses it as a
 *   connection indicator: LED on (stored colour) while connected, LED off
 *   (all channels 0) when disconnected.  The config is scoped to a single
 *   connection lifecycle — it is cleared on every disconnect so the next
 *   session starts with the LED off.
 *
 * Diagnostics behaviour:
 *   When "dx":true is present, the daemon appends a "_m" timing object to
 *   every "t":0 response envelope for the rest of the session:
 *     "_m":{"pr":<ms>,"dp":<ms>,"ex":<ms>,"sr":<ms>,"tt":<ms>}
 *   pr=parse, dp=dispatch, ex=execute, sr=serialize, tt=total (all in ms).
 *   This flag is cleared on every disconnect so each new session starts with
 *   diagnostics disabled.
 *
 * Validation rules (enforced by heartbeat_apply_config):
 *   heartbeat_ms >= 500
 *   timeout_ms   >= 2000
 *   timeout_ms   >  heartbeat_ms
 *
 * Resources required: none.
 * Threading: main thread (FuriEventLoop).
 */

#pragma once

#include <stdint.h>
#include <stddef.h>

/**
 * Handle a "configure" request.
 *
 * @param id     Request ID from the JSON envelope.
 * @param json   Full JSON line; may contain heartbeat_ms, timeout_ms, led, and/or dx.
 * @param offset Byte offset past the already-parsed envelope fields.
 */
void configure_handler(uint32_t id, const char* json, size_t offset);
