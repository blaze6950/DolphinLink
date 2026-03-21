/**
 * ir_tx_raw.h — ir_tx_raw RPC handler declaration
 *
 * Command: ir_tx_raw
 *
 * Wire format (request):
 *   {"id":N,"cmd":"ir_tx_raw","timings":[9000,4500,560,...]}
 *     timings — array of mark/space durations in microseconds (max 512 values)
 *               even-indexed values are marks (carrier on), odd are spaces
 *
 * Wire format (response):
 *   {"t":0,"i":N}
 *
 * Error codes:
 *   missing_timings — "timings" array absent or empty
 *
 * Resources: RESOURCE_IR (pre-acquired by the dispatcher)
 *
 * The handler blocks on a semaphore until the InfraredWorker fires the
 * "signal sent" callback (max 1000 ms for 512 timings × ~1 ms each).
 * Carrier frequency is fixed at 38 kHz with 33% duty cycle.
 */

#pragma once

#include <stdint.h>

/**
 * Handle an "ir_tx_raw" request.
 *
 * Transmits a raw timing array as an infrared burst synchronously.
 *
 * @param id   Request ID echoed in the response.
 * @param json Full JSON request line.
 */
void ir_tx_raw_handler(uint32_t id, const char* json);
