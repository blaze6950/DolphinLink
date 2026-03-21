/**
 * gpio_watch_start.h — gpio_watch_start RPC handler declaration
 *
 * Command: gpio_watch_start  (streaming)
 *
 * Wire format (request):
 *   {"id":N,"cmd":"gpio_watch_start","pin":"1"}
 *     pin — external connector pin label ("1"–"8")
 *
 * Wire format (stream opened):
 *   {"t":0,"i":N,"p":{"stream":M}}
 *
 * Wire format (stream event — emitted on every rising or falling edge):
 *   {"t":1,"i":M,"p":{"pin":"1","level":true}}
 *     pin   — pin label echoed from the request
 *     level — current digital level sampled inside the EXTI ISR
 *
 * Error codes:
 *   missing_pin     — "pin" field absent
 *   invalid_pin     — pin label not found in the pin table
 *   stream_table_full — no free stream slots
 *
 * Resources: none (GPIO EXTI does not require a shared resource token)
 *
 * ISR constraint: gpio_exti_callback runs in interrupt context.
 * It only calls furi_hal_gpio_read() and furi_message_queue_put().
 * The JSON fragment is pre-composed at stream-open time to avoid snprintf
 * inside the ISR.
 */

#pragma once

#include <stdint.h>

/**
 * Handle a "gpio_watch_start" request.
 *
 * Allocates a stream slot, registers a GPIO EXTI interrupt on both edges of
 * the named pin, and sends the stream-opened response.  Edge events are
 * subsequently posted to stream_event_queue by gpio_exti_callback and
 * serialised to the host by the main event loop.
 *
 * @param id   Request ID echoed in the response.
 * @param json Full JSON request line.
 */
void gpio_watch_start_handler(uint32_t id, const char* json);
