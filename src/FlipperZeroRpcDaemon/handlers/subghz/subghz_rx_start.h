/**
 * subghz_rx_start.h — subghz_rx_start RPC handler declaration
 *
 * Command: subghz_rx_start  (streaming)
 *
 * Wire format (request):
 *   {"id":N,"cmd":"subghz_rx_start","freq":433920000}
 *     freq — carrier frequency in Hz (default: 433920000)
 *
 * Wire format (stream opened):
 *   {"t":0,"i":N,"p":{"stream":M}}
 *
 * Wire format (stream event — emitted for each raw pair):
 *   {"t":1,"i":M,"p":{"level":true,"duration_us":9000}}
 *     level       — true = mark (carrier on), false = space (carrier off)
 *     duration_us — duration in microseconds (uint32)
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

/**
 * Handle a "subghz_rx_start" request.
 *
 * Tunes the CC1101 to the requested frequency, starts the SubGhzWorker,
 * allocates a stream slot, and sends the stream-opened response.
 *
 * @param id   Request ID echoed in the response.
 * @param json Full JSON request line.
 */
void subghz_rx_start_handler(uint32_t id, const char* json);
