/**
 * rpc_handlers_system.c — System / device-info RPC handler implementations
 */

#include "rpc_handlers_system.h"
#include "../core/rpc_response.h"
#include "../core/rpc_json.h"
#include "../core/rpc_cmd_log.h"

#include <furi.h>
#include <furi_hal_version.h>
#include <furi_hal_power.h>
#include <furi_hal_rtc.h>
#include <furi_hal_region.h>
#include <stdio.h>
#include <string.h>
#include <inttypes.h>

/* =========================================================
 * device_info
 * ========================================================= */

void device_info_handler(uint32_t id, const char* json) {
    UNUSED(json);

    const char* name = furi_hal_version_get_name_ptr();
    if(!name) name = "unknown";

    const Version* ver = furi_hal_version_get_firmware_version();
    const char* fw_ver = ver ? version_get_version(ver) : "unknown";
    const char* fw_build = ver ? version_get_builddate(ver) : "unknown";

    /* UID is 8 bytes; format as hex string */
    const uint8_t* uid = furi_hal_version_uid();
    char uid_str[17] = {0};
    if(uid) {
        snprintf(
            uid_str,
            sizeof(uid_str),
            "%02X%02X%02X%02X%02X%02X%02X%02X",
            uid[0],
            uid[1],
            uid[2],
            uid[3],
            uid[4],
            uid[5],
            uid[6],
            uid[7]);
    } else {
        strncpy(uid_str, "0000000000000000", sizeof(uid_str));
    }

    char resp[320];
    snprintf(
        resp,
        sizeof(resp),
        "{\"id\":%" PRIu32
        ",\"status\":\"ok\",\"data\":{\"name\":\"%s\",\"firmware\":\"%s\",\"build_date\":\"%s\","
        "\"uid\":\"%s\"}}\n",
        id,
        name,
        fw_ver,
        fw_build,
        uid_str);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " device_info -> ok", id);

    rpc_send_response(resp, log_entry);
}

/* =========================================================
 * power_info
 * ========================================================= */

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
        "{\"id\":%" PRIu32 ",\"status\":\"ok\",\"data\":{\"pct\":%" PRIu8 ",\"charging\":%s"
        ",\"voltage_mv\":%" PRIi32 ",\"current_ma\":%" PRIi32 "}}\n",
        id,
        pct,
        charging ? "true" : "false",
        voltage_mv,
        current_ma);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " power_info -> ok", id);

    rpc_send_response(resp, log_entry);
}

/* =========================================================
 * datetime_get
 * ========================================================= */

void datetime_get_handler(uint32_t id, const char* json) {
    UNUSED(json);

    DateTime dt;
    furi_hal_rtc_get_datetime(&dt);

    char resp[256];
    snprintf(
        resp,
        sizeof(resp),
        "{\"id\":%" PRIu32 ",\"status\":\"ok\",\"data\":{\"year\":%" PRIu16 ",\"month\":%" PRIu8
        ",\"day\":%" PRIu8 ",\"hour\":%" PRIu8 ",\"minute\":%" PRIu8 ",\"second\":%" PRIu8 "}}\n",
        id,
        dt.year,
        dt.month,
        dt.day,
        dt.hour,
        dt.minute,
        dt.second);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " datetime_get -> ok", id);

    rpc_send_response(resp, log_entry);
}

/* =========================================================
 * datetime_set
 * ========================================================= */

void datetime_set_handler(uint32_t id, const char* json) {
    uint32_t year = 0, month = 0, day = 0, hour = 0, minute = 0, second = 0;

    json_extract_uint32(json, "year", &year);
    json_extract_uint32(json, "month", &month);
    json_extract_uint32(json, "day", &day);
    json_extract_uint32(json, "hour", &hour);
    json_extract_uint32(json, "minute", &minute);
    json_extract_uint32(json, "second", &second);

    if(year == 0 || month == 0 || day == 0) {
        rpc_send_error(id, "missing_datetime_fields", "datetime_set");
        return;
    }

    DateTime dt = {
        .year = (uint16_t)year,
        .month = (uint8_t)month,
        .day = (uint8_t)day,
        .hour = (uint8_t)hour,
        .minute = (uint8_t)minute,
        .second = (uint8_t)second,
        .weekday = 1, /* not used by RTC */
    };
    furi_hal_rtc_set_datetime(&dt);

    rpc_send_ok(id, "datetime_set");
}

/* =========================================================
 * region_info
 * ========================================================= */

void region_info_handler(uint32_t id, const char* json) {
    UNUSED(json);

    const FuriHalRegion* region = furi_hal_region_get();
    const char* region_name = furi_hal_region_get_name();
    if(!region_name) region_name = "unknown";

    /* Build a compact JSON array of allowed bands */
    char bands_buf[512];
    size_t pos = 0;
    bands_buf[pos++] = '[';

    if(region) {
        for(size_t i = 0; i < region->bands_count; i++) {
            if(i > 0 && pos < sizeof(bands_buf) - 1) bands_buf[pos++] = ',';
            int written = snprintf(
                bands_buf + pos,
                sizeof(bands_buf) - pos,
                "{\"start\":%" PRIu32 ",\"end\":%" PRIu32 ",\"power_limit\":%" PRIu8 "}",
                region->bands[i].start,
                region->bands[i].end,
                region->bands[i].power_limit);
            if(written > 0) pos += (size_t)written;
            if(pos >= sizeof(bands_buf) - 2) break;
        }
    }
    if(pos < sizeof(bands_buf) - 1) bands_buf[pos++] = ']';
    bands_buf[pos] = '\0';

    char resp[640];
    snprintf(
        resp,
        sizeof(resp),
        "{\"id\":%" PRIu32 ",\"status\":\"ok\",\"data\":{\"region\":\"%s\",\"bands\":%s}}\n",
        id,
        region_name,
        bands_buf);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " region_info -> ok", id);

    rpc_send_response(resp, log_entry);
}

/* =========================================================
 * frequency_is_allowed
 * ========================================================= */

void frequency_is_allowed_handler(uint32_t id, const char* json) {
    uint32_t freq = 0;
    if(!json_extract_uint32(json, "freq", &freq)) {
        rpc_send_error(id, "missing_freq", "frequency_is_allowed");
        return;
    }

    bool allowed = furi_hal_region_is_frequency_allowed(freq);

    char resp[128];
    snprintf(
        resp,
        sizeof(resp),
        "{\"id\":%" PRIu32 ",\"status\":\"ok\",\"data\":{\"allowed\":%s}}\n",
        id,
        allowed ? "true" : "false");

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(
        log_entry, sizeof(log_entry), "#%" PRIu32 " freq_allowed -> %s", id, allowed ? "y" : "n");

    rpc_send_response(resp, log_entry);
}
