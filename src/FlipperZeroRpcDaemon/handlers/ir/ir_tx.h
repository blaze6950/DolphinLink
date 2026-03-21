/**
 * ir_tx.h — ir_tx RPC handler declaration
 *
 * Command: ir_tx
 *
 * Wire format (request):
 *   {"id":N,"cmd":"ir_tx","protocol":"NEC","address":0,"command":0}
 *     protocol — infrared protocol name (e.g. "NEC", "Samsung32", "RC6")
 *     address  — device address (uint32)
 *     command  — command code (uint32)
 *
 * Wire format (response):
 *   {"t":0,"i":N}
 *
 * Error codes:
 *   missing_protocol — "protocol" field absent
 *   unknown_protocol — protocol name not recognised by the Infrared SDK
 *
 * Resources: RESOURCE_IR (pre-acquired by the dispatcher)
 *
 * The handler blocks on a semaphore until InfraredWorker fires the
 * "message sent" callback (max ~500 ms timeout for a typical NEC frame).
 */

#pragma once

#include <stdint.h>

/**
 * Handle an "ir_tx" request.
 *
 * Transmits a single decoded infrared frame synchronously.
 *
 * @param id   Request ID echoed in the response.
 * @param json Full JSON request line.
 */
void ir_tx_handler(uint32_t id, const char* json);
