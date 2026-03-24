/**
 * ir_tx.h — ir_tx RPC handler declaration
 *
 * Command: ir_tx
 *
 * Wire format (request):
 *   {"c":17,"i":N,"pr":"NEC","a":0,"cm":0,"rp":0}
 *     pr — infrared protocol name (e.g. "NEC", "Samsung32", "RC6")
 *     a  — device address (uint32)
 *     cm — command code (uint32)
 *     rp — repeat flag (0 or 1)
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
#include <stddef.h>

/**
 * Handle an "ir_tx" request.
 *
 * @param id     Request ID echoed in the response.
 * @param json   Full JSON request line.
 * @param offset Byte offset past the already-parsed envelope fields.
 */
void ir_tx_handler(uint32_t id, const char* json, size_t offset);
