/**
 * subghz_get_rssi.h — subghz_get_rssi RPC handler declaration
 *
 * Command: subghz_get_rssi
 *
 * Wire format (request):
 *   {"id":N,"cmd":"subghz_get_rssi","freq":433920000}
 *     freq — frequency to tune to in Hz (default: 433920000)
 *
 * Wire format (response):
 *   {"t":0,"i":N,"p":{"rssi_dbm10":-750}}
 *     rssi_dbm10 — RSSI in tenths-of-dBm (integer, e.g. -750 = -75.0 dBm)
 *
 * Error codes:
 *   resource_busy — RESOURCE_SUBGHZ is held by another stream
 *
 * Resources: RESOURCE_SUBGHZ (checked and acquired inside the handler,
 *            released before returning)
 *
 * The radio is powered up for ~5 ms to allow AGC to settle, then RSSI
 * is sampled and the radio is put back to sleep.
 */

#pragma once

#include <stdint.h>

/**
 * Handle a "subghz_get_rssi" request.
 *
 * Briefly enables the CC1101 receiver, samples RSSI, then sleeps the radio.
 *
 * @param id   Request ID echoed in the response.
 * @param json Full JSON request line.
 */
void subghz_get_rssi_handler(uint32_t id, const char* json);
