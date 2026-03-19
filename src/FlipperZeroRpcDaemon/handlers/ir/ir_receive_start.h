/**
 * ir_receive_start.h — ir_receive_start RPC handler declaration
 *
 * Command: ir_receive_start  (streaming)
 *
 * Wire format (request):
 *   {"id":N,"cmd":"ir_receive_start"}
 *
 * Wire format (stream opened):
 *   {"id":N,"stream":M}
 *
 * Wire format (stream event — emitted for each decoded infrared frame):
 *   {"event":{"protocol":"NEC","address":0,"command":0,"repeat":false},"stream":M}
 *     protocol — infrared protocol name
 *     address  — device address (uint32)
 *     command  — command code (uint32)
 *     repeat   — true if this is a repeat frame
 *
 * Error codes:
 *   stream_table_full — no free stream slots
 *
 * Resources: RESOURCE_IR (pre-acquired by the dispatcher)
 *
 * The InfraredWorker callback fires on the InfraredWorker thread.
 * furi_message_queue_put() is safe there.  Only decoded signals are forwarded.
 */

#pragma once

#include <stdint.h>

/**
 * Handle an "ir_receive_start" request.
 *
 * Starts the InfraredWorker in receive mode, allocates a stream slot, and
 * sends the stream-opened response.  Decoded frames are posted to
 * stream_event_queue by the worker callback.
 *
 * @param id   Request ID echoed in the response.
 * @param json Full JSON request line (unused).
 */
void ir_receive_start_handler(uint32_t id, const char* json);
