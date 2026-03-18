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

    /* --- Identity -------------------------------------------------------- */
    const char* name = furi_hal_version_get_name_ptr();
    if(!name) name = "unknown";

    const char* model = furi_hal_version_get_model_name();
    if(!model) model = "unknown";

    const char* model_code = furi_hal_version_get_model_code();
    if(!model_code) model_code = "unknown";

    /* --- Firmware version ------------------------------------------------ */
    const Version* ver = furi_hal_version_get_firmware_version();
    const char* fw_ver    = ver ? version_get_version(ver)          : "unknown";
    const char* fw_origin = ver ? version_get_firmware_origin(ver)  : "unknown";
    const char* fw_build  = ver ? version_get_builddate(ver)        : "unknown";
    const char* git_hash  = ver ? version_get_githash(ver)          : "unknown";
    const char* git_branch    = ver ? version_get_gitbranch(ver)    : "unknown";
    const char* git_branchnum = ver ? version_get_gitbranchnum(ver) : "unknown";
    const char* git_origin    = ver ? version_get_git_origin(ver)   : "unknown";
    bool        dirty         = ver ? version_get_dirty_flag(ver)   : false;

    if(!fw_ver)         fw_ver         = "unknown";
    if(!fw_origin)      fw_origin      = "unknown";
    if(!fw_build)       fw_build       = "unknown";
    if(!git_hash)       git_hash       = "unknown";
    if(!git_branch)     git_branch     = "unknown";
    if(!git_branchnum)  git_branchnum  = "unknown";
    if(!git_origin)     git_origin     = "unknown";

    /* --- Hardware OTP fields --------------------------------------------- */
    uint8_t hw_version  = furi_hal_version_get_hw_version();
    uint8_t hw_target   = furi_hal_version_get_hw_target();
    uint8_t hw_body     = furi_hal_version_get_hw_body();
    uint8_t hw_color    = (uint8_t)furi_hal_version_get_hw_color();
    uint8_t hw_connect  = furi_hal_version_get_hw_connect();
    uint8_t hw_display  = (uint8_t)furi_hal_version_get_hw_display();
    uint8_t hw_region   = (uint8_t)furi_hal_version_get_hw_region();
    uint32_t hw_ts      = furi_hal_version_get_hw_timestamp();

    const char* hw_region_name = furi_hal_version_get_hw_region_name();
    if(!hw_region_name) hw_region_name = "unknown";

    /* --- Regulatory IDs -------------------------------------------------- */
    const char* fcc_id  = furi_hal_version_get_fcc_id();
    const char* ic_id   = furi_hal_version_get_ic_id();
    const char* mic_id  = furi_hal_version_get_mic_id();
    const char* srrc_id = furi_hal_version_get_srrc_id();
    const char* ncc_id  = furi_hal_version_get_ncc_id();
    if(!fcc_id)  fcc_id  = "unknown";
    if(!ic_id)   ic_id   = "unknown";
    if(!mic_id)  mic_id  = "unknown";
    if(!srrc_id) srrc_id = "unknown";
    if(!ncc_id)  ncc_id  = "unknown";

    /* --- UID (8 bytes → 16-char hex) ------------------------------------- */
    const uint8_t* uid = furi_hal_version_uid();
    char uid_str[17] = {0};
    if(uid) {
        snprintf(
            uid_str,
            sizeof(uid_str),
            "%02X%02X%02X%02X%02X%02X%02X%02X",
            uid[0], uid[1], uid[2], uid[3],
            uid[4], uid[5], uid[6], uid[7]);
    } else {
        strncpy(uid_str, "0000000000000000", sizeof(uid_str));
    }

    /* --- BLE MAC (6 bytes → 12-char hex) --------------------------------- */
    const uint8_t* ble_mac = furi_hal_version_get_ble_mac();
    char ble_mac_str[13] = {0};
    if(ble_mac) {
        snprintf(
            ble_mac_str,
            sizeof(ble_mac_str),
            "%02X%02X%02X%02X%02X%02X",
            ble_mac[0], ble_mac[1], ble_mac[2],
            ble_mac[3], ble_mac[4], ble_mac[5]);
    } else {
        strncpy(ble_mac_str, "000000000000", sizeof(ble_mac_str));
    }

    /* --- Build response incrementally ------------------------------------ */
    char resp[1536];
    int pos = 0;

    pos += snprintf(resp + pos, sizeof(resp) - pos,
        "{\"id\":%" PRIu32 ",\"status\":\"ok\",\"data\":{", id);

    /* Identity */
    pos += snprintf(resp + pos, sizeof(resp) - pos,
        "\"name\":\"%s\"", name);
    pos += snprintf(resp + pos, sizeof(resp) - pos,
        ",\"model\":\"%s\"", model);
    pos += snprintf(resp + pos, sizeof(resp) - pos,
        ",\"model_code\":\"%s\"", model_code);

    /* Firmware */
    pos += snprintf(resp + pos, sizeof(resp) - pos,
        ",\"firmware\":\"%s\"", fw_ver);
    pos += snprintf(resp + pos, sizeof(resp) - pos,
        ",\"firmware_origin\":\"%s\"", fw_origin);
    pos += snprintf(resp + pos, sizeof(resp) - pos,
        ",\"build_date\":\"%s\"", fw_build);
    pos += snprintf(resp + pos, sizeof(resp) - pos,
        ",\"git_hash\":\"%s\"", git_hash);
    pos += snprintf(resp + pos, sizeof(resp) - pos,
        ",\"git_branch\":\"%s\"", git_branch);
    pos += snprintf(resp + pos, sizeof(resp) - pos,
        ",\"git_branch_num\":\"%s\"", git_branchnum);
    pos += snprintf(resp + pos, sizeof(resp) - pos,
        ",\"git_origin\":\"%s\"", git_origin);
    pos += snprintf(resp + pos, sizeof(resp) - pos,
        ",\"dirty\":%s", dirty ? "true" : "false");

    /* Hardware */
    pos += snprintf(resp + pos, sizeof(resp) - pos,
        ",\"hardware\":%" PRIu32, (uint32_t)hw_version);
    pos += snprintf(resp + pos, sizeof(resp) - pos,
        ",\"hw_target\":%" PRIu32, (uint32_t)hw_target);
    pos += snprintf(resp + pos, sizeof(resp) - pos,
        ",\"hw_body\":%" PRIu32, (uint32_t)hw_body);
    pos += snprintf(resp + pos, sizeof(resp) - pos,
        ",\"hw_color\":%" PRIu32, (uint32_t)hw_color);
    pos += snprintf(resp + pos, sizeof(resp) - pos,
        ",\"hw_connect\":%" PRIu32, (uint32_t)hw_connect);
    pos += snprintf(resp + pos, sizeof(resp) - pos,
        ",\"hw_display\":%" PRIu32, (uint32_t)hw_display);
    pos += snprintf(resp + pos, sizeof(resp) - pos,
        ",\"hw_region\":%" PRIu32, (uint32_t)hw_region);
    pos += snprintf(resp + pos, sizeof(resp) - pos,
        ",\"hw_region_name\":\"%s\"", hw_region_name);
    pos += snprintf(resp + pos, sizeof(resp) - pos,
        ",\"hw_timestamp\":%" PRIu32, hw_ts);

    /* Identity — UID and BLE MAC */
    pos += snprintf(resp + pos, sizeof(resp) - pos,
        ",\"uid\":\"%s\"", uid_str);
    pos += snprintf(resp + pos, sizeof(resp) - pos,
        ",\"ble_mac\":\"%s\"", ble_mac_str);

    /* Regulatory */
    pos += snprintf(resp + pos, sizeof(resp) - pos,
        ",\"fcc_id\":\"%s\"", fcc_id);
    pos += snprintf(resp + pos, sizeof(resp) - pos,
        ",\"ic_id\":\"%s\"", ic_id);
    pos += snprintf(resp + pos, sizeof(resp) - pos,
        ",\"mic_id\":\"%s\"", mic_id);
    pos += snprintf(resp + pos, sizeof(resp) - pos,
        ",\"srrc_id\":\"%s\"", srrc_id);
    pos += snprintf(resp + pos, sizeof(resp) - pos,
        ",\"ncc_id\":\"%s\"", ncc_id);

    pos += snprintf(resp + pos, sizeof(resp) - pos, "}}\n");
    UNUSED(pos);

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
        "{\"id\":%" PRIu32 ",\"status\":\"ok\",\"data\":{\"charge\":%" PRIu8 ",\"charging\":%s"
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
