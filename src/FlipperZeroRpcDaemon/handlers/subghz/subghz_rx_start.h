/**
 * subghz_rx_start.h — subghz_rx_start RPC handler declaration
 *
 * Command: subghz_rx_start  (streaming)
 *
 * Wire format (request):
 *   {"c":22,"i":N,"fr":433920000}
 *     fr — carrier frequency in Hz (optional, default: 433920000)
 *
 * Wire format (stream opened):
 *   {"t":0,"i":N,"p":{"s":M}}
 *
 * Wire format (stream event — emitted for each raw pair):
 *   {"t":1,"i":M,"p":{"lv":true,"du":9000}}
 *     lv — true = mark (carrier on), false = space (carrier off)
 *     du — duration in microseconds (uint32)
 *
 * Error codes:
 *   stream_table_full — no free stream slots
 *
 * Resources: RESOURCE_SUBGHZ (pre-acquired by the dispatcher)
 *
 * The SubGhzWorker fires its pair callback on its own thread.
 * furi_message_queue_put() is safe there.
 */

#pragma once

#include <stdint.h>
#include <stddef.h>

/**
 * Handle a "subghz_rx_start" request.
 *
 * @param id     Request ID echoed in the response.
 * @param json   Full JSON request line.
 * @param offset Byte offset past the already-parsed envelope fields.
 */
void subghz_rx_start_handler(uint32_t id, const char* json, size_t offset);
