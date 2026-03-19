/**
 * adc_read.c — adc_read RPC handler implementation
 *
 * Reads a single ADC sample from a named GPIO pin and returns the raw count
 * and the converted millivolt value.
 *
 * Wire format (request):
 *   {"id":N,"cmd":"adc_read","pin":"1"}
 *
 * Wire format (response):
 *   {"id":N,"status":"ok","data":{"raw":2048,"mv":1650}}
 *     raw — 12-bit raw ADC count (0–4095)
 *     mv  — voltage in millivolts (integer)
 *
 * Error codes:
 *   missing_pin — "pin" field absent
 *   invalid_pin — label not found or not ADC-capable
 *
 * The ADC handle is acquired, sampled, and released within this call.
 * Voltage is encoded as integer millivolts to avoid %f formatting.
 */

#include "adc_read.h"
#include "gpio_pins.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"
#include "../../core/rpc_cmd_log.h"

#include <furi_hal_adc.h>
#include <stdio.h>
#include <inttypes.h>

void adc_read_handler(uint32_t id, const char* json) {
    char label[8] = {0};
    if(!json_extract_string(json, "pin", label, sizeof(label))) {
        rpc_send_error(id, "missing_pin", "adc_read");
        return;
    }
    const GpioPinEntry* entry = gpio_pin_entry_from_label(label);
    if(!entry || entry->adc_channel == FuriHalAdcChannelNone) {
        rpc_send_error(id, "invalid_pin", "adc_read");
        return;
    }

    FuriHalAdcHandle* adc = furi_hal_adc_acquire();
    furi_hal_adc_configure(adc);
    uint16_t raw = furi_hal_adc_read(adc, entry->adc_channel);
    float voltage = furi_hal_adc_convert_to_voltage(adc, raw);
    furi_hal_adc_release(adc);

    /* Encode voltage as millivolts integer to avoid %f */
    int32_t mv = (int32_t)(voltage * 1000.0f);

    char resp[128];
    snprintf(
        resp,
        sizeof(resp),
        "{\"id\":%" PRIu32 ",\"status\":\"ok\",\"data\":{\"raw\":%" PRIu16 ",\"mv\":%" PRIi32
        "}}\n",
        id,
        raw,
        mv);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(
        log_entry,
        sizeof(log_entry),
        "#%" PRIu32 " adc_read pin=%s -> %" PRIi32 "mv",
        id,
        label,
        mv);

    rpc_send_response(resp, log_entry);
}
