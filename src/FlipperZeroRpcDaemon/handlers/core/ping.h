/**
 * ping.h — ping command handler declaration
 *
 * Wire protocol:
 *   Request:  {"c":0,"i":N}
 *   Response: {"t":0,"i":N,"p":{"pg":true}}
 *
 * The ping command is a simple round-trip health-check with no arguments
 * and no resource requirements.
 */

#pragma once

#include <stdint.h>

/**
 * Handle a "ping" request.
 *
 * @param id   Request ID from the JSON envelope.
 * @param json Full JSON line (unused — ping takes no arguments).
 */
void ping_handler(uint32_t id, const char* json);
