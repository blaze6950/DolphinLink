/**
 * subghz_get_rssi.h — subghz_get_rssi RPC handler declaration
 *
 * Command: subghz_get_rssi
 *
 * Wire format (request):
 *   {"c":21,"i":N,"fr":433920000}
 *     fr — frequency to tune to in Hz (default: 433920000)
 *
 * Wire format (response):
 *   {"t":0,"i":N,"p":{"rs":-750}}
 *     rs — RSSI in tenths-of-dBm (integer, e.g. -750 = -75.0 dBm)
 *
 * Error codes:
 *   resource_busy — RESOURCE_SUBGHZ is held by another stream
 *
 * Resources: RESOURCE_SUBGHZ (dispatcher pre-checks; handler acquires and
 *            releases before returning)
 *
 * The radio is powered up for ~5 ms to allow AGC to settle, then RSSI
 * is sampled and the radio is put back to sleep.
 */

#pragma once

#include <stdint.h>
#include <stddef.h>

/**
 * Handle a "subghz_get_rssi" request.
 *
 * @param id     Request ID echoed in the response.
 * @param json   Full JSON request line.
 * @param offset Byte offset past the already-parsed envelope fields.
 */
void subghz_get_rssi_handler(uint32_t id, const char* json, size_t offset);
