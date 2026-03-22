/**
 * gpio_watch_start.h — gpio_watch_start RPC handler declaration
 *
 * Command: gpio_watch_start  (streaming)
 *
 * Wire format (request):
 *   {"c":16,"i":N,"p":<pin_enum>}
 *     p — pin enum integer
 *
 * Wire format (stream opened):
 *   {"t":0,"i":N,"p":{"s":M}}
 *
 * Wire format (stream event — emitted on every rising or falling edge):
 *   {"t":1,"i":M,"p":{"p":<pin_enum>,"lv":true}}
 *     p  — pin enum integer echoed from the request
 *     lv — current digital level sampled inside the EXTI ISR
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
#include <stddef.h>

/**
 * Handle a "gpio_watch_start" request.
 *
 * @param id     Request ID echoed in the response.
 * @param json   Full JSON request line.
 * @param offset Byte offset past the already-parsed envelope fields.
 */
void gpio_watch_start_handler(uint32_t id, const char* json, size_t offset);
