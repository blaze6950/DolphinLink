/**
 * ir_receive_start.h — ir_receive_start RPC handler declaration
 *
 * Command: ir_receive_start  (streaming)
 *
 * Wire format (request):
 *   {"c":19,"i":N}
 *
 * Wire format (stream opened):
 *   {"t":0,"i":N,"p":{"s":M}}
 *
 * Wire format (stream event — emitted for each decoded infrared frame):
 *   {"t":1,"i":M,"p":{"pr":"NEC","a":0,"cm":0,"rp":false}}
 *     pr — infrared protocol name
 *     a  — device address (uint32)
 *     cm — command code (uint32)
 *     rp — true if this is a repeat frame
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
#include <stddef.h>

/**
 * Handle an "ir_receive_start" request.
 *
 * @param id     Request ID echoed in the response.
 * @param json   Full JSON request line (unused).
 * @param offset Byte offset past the already-parsed envelope fields.
 */
void ir_receive_start_handler(uint32_t id, const char* json, size_t offset);
