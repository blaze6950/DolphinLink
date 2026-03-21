/**
 * configure.h — configure command handler declaration
 *
 * Propagates host-side configuration to the daemon during session startup.
 * The client sends this command immediately after daemon_info so the daemon
 * can align its behaviour (heartbeat timing) with the host's expectations.
 *
 * Wire protocol:
 *   Request:  {"id":N,"cmd":"configure","heartbeat_ms":<u32>,"timeout_ms":<u32>}
 *
 *   Both arguments are optional.  If a field is absent the daemon retains its
 *   current value (initially the compile-time default).
 *
 *   Response (success):
 *     {"t":0,"i":N,"p":{"heartbeat_ms":<u32>,"timeout_ms":<u32>}}
 *   The response carries the *effective* values the daemon is now using,
 *   so the client can confirm what was accepted (e.g. after clamping).
 *
 *   Errors:
 *     invalid_config — the supplied values failed validation:
 *                      heartbeat_ms < 500, timeout_ms < 2000,
 *                      or timeout_ms <= heartbeat_ms.
 *                      No values are changed on error.
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

/**
 * Handle a "configure" request.
 *
 * @param id   Request ID from the JSON envelope.
 * @param json Full JSON line; may contain heartbeat_ms and/or timeout_ms.
 */
void configure_handler(uint32_t id, const char* json);
