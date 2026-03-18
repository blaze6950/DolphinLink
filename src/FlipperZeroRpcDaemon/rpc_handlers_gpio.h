/**
 * rpc_handlers_gpio.h — GPIO / ADC RPC handler declarations
 *
 * Commands handled here:
 *   gpio_read          — read digital level of a GPIO pin
 *   gpio_write         — set digital level of a GPIO pin
 *   adc_read           — read ADC voltage on a GPIO pin
 *   gpio_set_5v        — enable / disable the 5 V header supply rail
 *   gpio_watch_start   — stream digital edge events on a GPIO pin (migrated from rpc_handlers.c)
 */

#pragma once

#include <stdint.h>

void gpio_read_handler(uint32_t id, const char* json);
void gpio_write_handler(uint32_t id, const char* json);
void adc_read_handler(uint32_t id, const char* json);
void gpio_set_5v_handler(uint32_t id, const char* json);
void gpio_watch_start_handler(uint32_t id, const char* json);
