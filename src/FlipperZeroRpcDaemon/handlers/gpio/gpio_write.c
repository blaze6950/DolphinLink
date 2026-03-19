/**
 * gpio_write.c — gpio_write RPC handler implementation
 *
 * Drives a named external GPIO pin to a specified digital level.
 *
 * Wire format (request):
 *   {"id":N,"cmd":"gpio_write","pin":"1","level":true}
 *
 * Wire format (response):
 *   {"id":N,"status":"ok"}
 *
 * Error codes:
 *   missing_pin   — "pin" field absent
 *   invalid_pin   — label not in pin table
 *   missing_level — "level" field absent
 */

#include "gpio_write.h"
#include "gpio_pins.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"

#include <furi_hal_gpio.h>

void gpio_write_handler(uint32_t id, const char* json) {
    char label[8] = {0};
    const char* cursor = json;
    if(!json_extract_string_at(json, &cursor, "pin", label, sizeof(label))) {
        rpc_send_error(id, "missing_pin", "gpio_write");
        return;
    }
    const GpioPinEntry* entry = gpio_pin_entry_from_label(label);
    if(!entry) {
        rpc_send_error(id, "invalid_pin", "gpio_write");
        return;
    }

    bool level = false;
    if(!json_extract_bool_at(json, &cursor, "level", &level)) {
        rpc_send_error(id, "missing_level", "gpio_write");
        return;
    }

    furi_hal_gpio_init(entry->pin, GpioModeOutputPushPull, GpioPullNo, GpioSpeedLow);
    furi_hal_gpio_write(entry->pin, level);

    rpc_send_ok(id, "gpio_write");
}
