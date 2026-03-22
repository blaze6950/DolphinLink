/**
 * subghz_tx.h — subghz_tx RPC handler declaration
 *
 * Command: subghz_tx
 *
 * Wire format (request):
 *   {"c":20,"i":N,"lv":[9000,4500,...],"du":[...],"fr":433920000}
 *     lv  — mark level durations array in microseconds (max 512 values)
 *     du  — space durations array in microseconds
 *     fr  — carrier frequency in Hz (default: 433920000)
 *
 * Wire format (response):
 *   {"t":0,"i":N}
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
