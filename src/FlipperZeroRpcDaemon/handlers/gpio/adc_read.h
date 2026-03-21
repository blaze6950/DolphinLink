/**
 * adc_read.h — adc_read RPC handler declaration
 *
 * Command: adc_read
 *
 * Wire format (request):
 *   {"id":N,"cmd":"adc_read","pin":"1"}
 *     pin — external connector pin label ("1", "2", "3", "6", "7" are ADC-capable)
 *
 * Wire format (response):
 *   {"t":0,"i":N,"p":{"raw":2048,"mv":1650}}
 *     raw — raw 12-bit ADC count (0–4095)
 *     mv  — voltage in millivolts (integer)
 *
 * Error codes:
 *   missing_pin — "pin" field absent from request
 *   invalid_pin — pin label not found or pin has no ADC channel
 *
 * Resources: none (ADC is acquired and released within the call)
 */

#pragma once

#include <stdint.h>

/**
 * Handle an "adc_read" request.
 *
 * Acquires the ADC, takes a single sample, converts it to millivolts, then
 * releases the ADC.
 *
 * @param id   Request ID echoed in the response.
 * @param json Full JSON request line.
 */
void adc_read_handler(uint32_t id, const char* json);
