/**
 * gpio_read.c — gpio_read RPC handler implementation
 *
 * Reads the digital level of a named external GPIO pin.
 *
 * Wire format (request):
 *   {"c":12,"i":N,"p":<GpioPin int 1-8>}
 *
 * Wire format (response):
 *   {"t":0,"i":N,"p":{"lv":1}} or {"t":0,"i":N,"p":{"lv":0}}
 *
 * Error codes:
 *   missing_pin — "p" field absent
 *   invalid_pin — value not in pin table
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
    uint32_t pin_num = 0;
    if(!json_extract_uint32(json, "p", &pin_num) || pin_num < 1 || pin_num > 8) {
        rpc_send_error(id, "missing_pin", "gpio_read");
        return;
    }

    /* Map integer wire value to label string ("1"–"8") */
    char label[4];
    snprintf(label, sizeof(label), "%" PRIu32, pin_num);

    const GpioPinEntry* entry = gpio_pin_entry_from_label(label);
    if(!entry) {
        rpc_send_error(id, "invalid_pin", "gpio_read");
        return;
    }

    furi_hal_gpio_init(entry->pin, GpioModeInput, GpioPullUp, GpioSpeedLow);
    bool level = furi_hal_gpio_read(entry->pin);

    /* V1: bool as 1/0, wire key "lv" */
    char resp[16];
    snprintf(resp, sizeof(resp), "{\"lv\":%d}", level ? 1 : 0);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(
        log_entry, sizeof(log_entry), "#%" PRIu32 " gpio_read pin=%" PRIu32 " -> %d", id, pin_num, (int)level);

    rpc_send_data_response(id, resp, log_entry);
}
