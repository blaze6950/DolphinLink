/**
 * ping.h — ping command handler declaration
 *
 * Wire protocol:
 *   Request:  {"c":0,"i":N}
 *   Response: {"t":0,"i":N,"p":{"pg":1}}
 *
 * The ping command is a simple round-trip health-check with no arguments
 * and no resource requirements.
 */

#pragma once

#include <stdint.h>
#include <stddef.h>

/**
 * Handle a "ping" request.
 *
 * @param id     Request ID from the JSON envelope.
 * @param json   Full JSON line (unused — ping takes no arguments).
 * @param offset Byte offset past the already-parsed envelope fields.
 */
void ping_handler(uint32_t id, const char* json, size_t offset);
