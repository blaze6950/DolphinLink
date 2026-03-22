/**
 * gpio_write.c — gpio_write RPC handler implementation
 *
 * Drives a named external GPIO pin to a specified digital level.
 *
 * Wire format (request):
 *   {"c":13,"i":N,"p":<GpioPin int 1-8>,"lv":0|1}
 *
 * Wire format (response):
 *   {"t":0,"i":N}
 *
 * Error codes:
 *   missing_pin   — "p" field absent or out of range
 *   invalid_pin   — value not in pin table
 *   missing_level — "lv" field absent
 */

#include "gpio_write.h"
#include "gpio_pins.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"

#include <furi_hal_gpio.h>
#include <stdio.h>
#include <inttypes.h>

void gpio_write_handler(uint32_t id, const char* json) {
    uint32_t pin_num = 0;
    const char* cursor = json;
    if(!json_extract_uint32_at(json, &cursor, "p", &pin_num) || pin_num < 1 || pin_num > 8) {
        rpc_send_error(id, "missing_pin", "gpio_write");
        return;
    }

    /* Map integer wire value to label string ("1"–"8") */
    char label[4];
    snprintf(label, sizeof(label), "%" PRIu32, pin_num);

    const GpioPinEntry* entry = gpio_pin_entry_from_label(label);
    if(!entry) {
        rpc_send_error(id, "invalid_pin", "gpio_write");
        return;
    }

    bool level = false;
    if(!json_extract_bool_at(json, &cursor, "lv", &level)) {
        rpc_send_error(id, "missing_level", "gpio_write");
        return;
    }

    furi_hal_gpio_init(entry->pin, GpioModeOutputPushPull, GpioPullNo, GpioSpeedLow);
    furi_hal_gpio_write(entry->pin, level);

    rpc_send_ok(id, "gpio_write");
}
