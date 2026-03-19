/**
 * stream_close.h — stream_close command handler declaration
 *
 * Wire protocol:
 *   Request:  {"id":N,"cmd":"stream_close","stream":M}
 *   Response: {"id":N,"status":"ok"}
 *   Errors:   missing_stream_id, stream_not_found
 *
 * Closes the stream identified by the "stream" field, invoking its
 * hardware teardown callback and releasing all associated resources.
 */

#pragma once

#include <stdint.h>

/**
 * Handle a "stream_close" request.
 *
 * @param id   Request ID from the JSON envelope.
 * @param json Full JSON line; must contain a "stream" field (uint32).
 */
void stream_close_handler(uint32_t id, const char* json);
