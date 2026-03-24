/**
 * device_info.c — device_info command handler implementation
 *
 * Wire protocol:
 *   Request:  {"c":5,"i":N}
 *   Response: {"t":0,"i":N,"p":{...}} (see device_info.h)
 *
 * Reads identity, firmware, hardware OTP, regulatory IDs and UID/BLE MAC
 * via furi_hal_version_* APIs.  The response buffer is built incrementally
 * using chained snprintf() calls.
 *
 * Resources required: none.
 * Threading: main thread (FuriEventLoop).
 */

#include "device_info.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_cmd_log.h"

#include <furi.h>
#include <furi_hal_version.h>
#include <stdio.h>
#include <string.h>
#include <inttypes.h>

void device_info_handler(uint32_t id, const char* json, size_t offset) {
    (void)json;
    (void)offset;

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

    /* --- Build payload incrementally ------------------------------------- */
    char resp[1536];
    int pos = 0;

    pos += snprintf(resp + pos, sizeof(resp) - pos, "{");

    /* Identity */
    pos += snprintf(resp + pos, sizeof(resp) - pos, "\"nm\":\"%s\"", name);
    pos += snprintf(resp + pos, sizeof(resp) - pos, ",\"m\":\"%s\"", model);
    pos += snprintf(resp + pos, sizeof(resp) - pos, ",\"mc\":\"%s\"", model_code);

    /* Firmware */
    pos += snprintf(resp + pos, sizeof(resp) - pos, ",\"fw\":\"%s\"", fw_ver);
    pos += snprintf(resp + pos, sizeof(resp) - pos, ",\"fo\":\"%s\"", fw_origin);
    pos += snprintf(resp + pos, sizeof(resp) - pos, ",\"bd\":\"%s\"", fw_build);
    pos += snprintf(resp + pos, sizeof(resp) - pos, ",\"gh\":\"%s\"", git_hash);
    pos += snprintf(resp + pos, sizeof(resp) - pos, ",\"gb\":\"%s\"", git_branch);
    pos += snprintf(resp + pos, sizeof(resp) - pos, ",\"gbn\":\"%s\"", git_branchnum);
    pos += snprintf(resp + pos, sizeof(resp) - pos, ",\"go\":\"%s\"", git_origin);
    pos += snprintf(resp + pos, sizeof(resp) - pos, ",\"dy\":%d", dirty ? 1 : 0);

    /* Hardware */
    pos += snprintf(resp + pos, sizeof(resp) - pos, ",\"hw\":%" PRIu32, (uint32_t)hw_version);
    pos += snprintf(resp + pos, sizeof(resp) - pos, ",\"hwt\":%" PRIu32, (uint32_t)hw_target);
    pos += snprintf(resp + pos, sizeof(resp) - pos, ",\"hwb\":%" PRIu32, (uint32_t)hw_body);
    pos += snprintf(resp + pos, sizeof(resp) - pos, ",\"hwc\":%" PRIu32, (uint32_t)hw_color);
    pos += snprintf(resp + pos, sizeof(resp) - pos, ",\"hwcn\":%" PRIu32, (uint32_t)hw_connect);
    pos += snprintf(resp + pos, sizeof(resp) - pos, ",\"hwd\":%" PRIu32, (uint32_t)hw_display);
    pos += snprintf(resp + pos, sizeof(resp) - pos, ",\"hwr\":%" PRIu32, (uint32_t)hw_region);
    pos += snprintf(resp + pos, sizeof(resp) - pos, ",\"hwrn\":\"%s\"", hw_region_name);
    pos += snprintf(resp + pos, sizeof(resp) - pos, ",\"hwts\":%" PRIu32, hw_ts);

    /* Identity — UID and BLE MAC */
    pos += snprintf(resp + pos, sizeof(resp) - pos, ",\"uid\":\"%s\"", uid_str);
    pos += snprintf(resp + pos, sizeof(resp) - pos, ",\"bm\":\"%s\"", ble_mac_str);

    /* Regulatory */
    pos += snprintf(resp + pos, sizeof(resp) - pos, ",\"fcc\":\"%s\"", fcc_id);
    pos += snprintf(resp + pos, sizeof(resp) - pos, ",\"ic\":\"%s\"", ic_id);
    pos += snprintf(resp + pos, sizeof(resp) - pos, ",\"mic\":\"%s\"", mic_id);
    pos += snprintf(resp + pos, sizeof(resp) - pos, ",\"srrc\":\"%s\"", srrc_id);
    pos += snprintf(resp + pos, sizeof(resp) - pos, ",\"ncc\":\"%s\"", ncc_id);

    pos += snprintf(resp + pos, sizeof(resp) - pos, "}");
    UNUSED(pos);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " device_info -> ok", id);

    rpc_send_data_response(id, resp, log_entry);
}
