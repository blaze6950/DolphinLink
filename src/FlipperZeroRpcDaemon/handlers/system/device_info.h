/**
 * device_info.h — device_info command handler declaration
 *
 * Wire protocol:
 *   Request:  {"id":N,"cmd":"device_info"}
 *   Response: {"t":0,"i":N,"p":{
 *               "name":"<str>","model":"<str>","model_code":"<str>",
 *               "firmware":"<str>","firmware_origin":"<str>","build_date":"<str>",
 *               "git_hash":"<str>","git_branch":"<str>","git_branch_num":"<str>",
 *               "git_origin":"<str>","dirty":<bool>,
 *               "hardware":<u32>,"hw_target":<u32>,"hw_body":<u32>,
 *               "hw_color":<u32>,"hw_connect":<u32>,"hw_display":<u32>,
 *               "hw_region":<u32>,"hw_region_name":"<str>","hw_timestamp":<u32>,
 *               "uid":"<16 hex chars>","ble_mac":"<12 hex chars>",
 *               "fcc_id":"<str>","ic_id":"<str>","mic_id":"<str>",
 *               "srrc_id":"<str>","ncc_id":"<str>"}}
 *
 * Resources required: none.
 */

#pragma once

#include <stdint.h>

/**
 * Handle a "device_info" request.
 *
 * @param id   Request ID from the JSON envelope.
 * @param json Full JSON line (unused — no arguments).
 */
void device_info_handler(uint32_t id, const char* json);
