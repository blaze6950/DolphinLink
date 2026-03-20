/**
 * power_info.c — power_info command handler implementation
 *
 * Wire protocol:
 *   Request:  {"id":N,"cmd":"power_info"}
 *   Response: {"type":"response","id":N,"payload":{
 *               "charge":<u8>,"charging":<bool>,
 *               "voltage_mv":<i32>,"current_ma":<i32>}}
 *
 * Voltage and current are returned as integer millivolts / milliamps to
 * avoid floating-point format specifiers on the embedded target.
 *
 * Resources required: none.
 * Threading: main thread (FuriEventLoop).
 */

#include "power_info.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_cmd_log.h"

#include <furi.h>
#include <furi_hal_power.h>
#include <stdio.h>
#include <inttypes.h>

void power_info_handler(uint32_t id, const char* json) {
    UNUSED(json);

    uint8_t pct = furi_hal_power_get_pct();
    bool charging = furi_hal_power_is_charging();
    float voltage = furi_hal_power_get_battery_voltage(FuriHalPowerICFuelGauge);
    float current = furi_hal_power_get_battery_current(FuriHalPowerICFuelGauge);

    /* Encode floats as fixed-point integers (mV, mA) to avoid %f */
    int32_t voltage_mv = (int32_t)(voltage * 1000.0f);
    int32_t current_ma = (int32_t)(current * 1000.0f);

    char resp[256];
    snprintf(
        resp,
        sizeof(resp),
        "{\"charge\":%" PRIu8 ",\"charging\":%s"
        ",\"voltage_mv\":%" PRIi32 ",\"current_ma\":%" PRIi32 "}",
        pct,
        charging ? "true" : "false",
        voltage_mv,
        current_ma);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " power_info -> ok", id);

    rpc_send_data_response(id, resp, log_entry);
}
