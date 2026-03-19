/**
 * gpio_read.c — gpio_read RPC handler implementation
 *
 * Reads the digital level of a named external GPIO pin.
 *
 * Wire format (request):
 *   {"id":N,"cmd":"gpio_read","pin":"1"}
 *
 * Wire format (response):
 *   {"id":N,"status":"ok","data":{"level":true}}
 *
 * Error codes:
 *   missing_pin — "pin" field absent
 *   invalid_pin — label not in pin table
 */

#include "gpio_read.h"
#include "gpio_pins.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"
#include "../../core/rpc_cmd_log.h"

#include <furi_hal_gpio.h>
#include <stdio.h>
#include <inttypes.h>

void gpio_read_handler(uint32_t id, const char* json) {
    char label[8] = {0};
    if(!json_extract_string(json, "pin", label, sizeof(label))) {
        rpc_send_error(id, "missing_pin", "gpio_read");
        return;
    }
    const GpioPinEntry* entry = gpio_pin_entry_from_label(label);
    if(!entry) {
        rpc_send_error(id, "invalid_pin", "gpio_read");
        return;
    }

    furi_hal_gpio_init(entry->pin, GpioModeInput, GpioPullUp, GpioSpeedLow);
    bool level = furi_hal_gpio_read(entry->pin);

    char resp[128];
    snprintf(
        resp,
        sizeof(resp),
        "{\"id\":%" PRIu32 ",\"status\":\"ok\",\"data\":{\"level\":%s}}\n",
        id,
        level ? "true" : "false");

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(
        log_entry, sizeof(log_entry), "#%" PRIu32 " gpio_read pin=%s -> %d", id, label, (int)level);

    rpc_send_response(resp, log_entry);
}
