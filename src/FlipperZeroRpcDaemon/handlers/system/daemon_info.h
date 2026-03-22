/**
 * daemon_info.h — daemon_info command handler declaration
 *
 * Returns a fixed descriptor block that the host can use for capability
 * negotiation before issuing other commands.
 *
 * Wire protocol:
 *   Request:  {"c":3,"i":N}
 *
 *   Response: {"t":0,"i":N,"p":{
 *               "n":"flipper_zero_rpc_daemon",
 *               "v":1,
 *               "cmds":["ping","stream_close","configure",...]}}
 *
 *   Fields:
 *     n    — Stable daemon identifier string.  Clients may check this to
 *             confirm they are talking to the correct FAP.
 *     v    — Monotonically increasing integer protocol version.  Increment
 *             whenever a breaking wire-format change is made.
 *     cmds — JSON array of every command name registered in rpc_dispatch.c,
 *             in dispatch-table order.  Clients use this list to detect
 *             whether a specific command is supported by the running daemon
 *             version before calling it.
 *
 * Resources required: none.
 */

#pragma once

#include <stdint.h>
#include <stddef.h>

/** Current daemon protocol version.  Bump on any breaking wire-format change. */
#define DAEMON_PROTOCOL_VERSION 1

/**
 * Handle a "daemon_info" request.
 *
 * @param id     Request ID from the JSON envelope.
 * @param json   Full JSON line (unused — no arguments).
 * @param offset Byte offset past the already-parsed envelope fields.
 */
void daemon_info_handler(uint32_t id, const char* json, size_t offset);
