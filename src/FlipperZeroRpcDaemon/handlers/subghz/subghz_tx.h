/**
 * subghz_tx.h — subghz_tx RPC handler declaration
 *
 * Command: subghz_tx
 *
 * Wire format (request):
 *   {"id":N,"cmd":"subghz_tx","timings":[9000,4500,...],"freq":433920000}
 *     timings — mark/space duration array in microseconds (max 512 values)
 *               even-indexed = mark (TX on), odd = space (TX off)
 *     freq    — carrier frequency in Hz (default: 433920000)
 *
 * Wire format (response):
 *   {"id":N,"status":"ok"}
 *
 * Error codes:
 *   resource_busy   — RESOURCE_SUBGHZ is held by another stream
 *   missing_timings — "timings" array absent or empty
 *
 * Resources: RESOURCE_SUBGHZ (checked and acquired inside the handler)
 *
 * The handler polls furi_hal_subghz_is_async_tx_complete() until done,
 * then puts the radio to sleep.  Max blocking time ≈ 512 ms.
 */

#pragma once

#include <stdint.h>

/**
 * Handle a "subghz_tx" request.
 *
 * Transmits a raw OOK timing array on the CC1101 synchronously.
 *
 * @param id   Request ID echoed in the response.
 * @param json Full JSON request line.
 */
void subghz_tx_handler(uint32_t id, const char* json);
