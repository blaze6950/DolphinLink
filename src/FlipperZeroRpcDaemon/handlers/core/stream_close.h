/**
 * stream_close.h — stream_close command handler declaration
 *
 * Wire protocol:
 *   Request:  {"c":1,"i":N,"s":M}
 *   Response: {"t":0,"i":N}
 *   Errors:   missing_stream_id, stream_not_found
 *
 * Closes the stream identified by the "s" field, invoking its
 * hardware teardown callback and releasing all associated resources.
 */

#pragma once

#include <stdint.h>

/**
 * Handle a "stream_close" request.
 *
 * @param id   Request ID from the JSON envelope.
 * @param json Full JSON line; must contain a "s" field (uint32).
 */
void stream_close_handler(uint32_t id, const char* json);
