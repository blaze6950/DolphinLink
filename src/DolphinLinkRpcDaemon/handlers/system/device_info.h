/**
 * device_info.h — device_info command handler declaration
 *
 * Wire protocol:
 *   Request:  {"c":5,"i":N}
 *   Response: {"t":0,"i":N,"p":{
 *               "nm":"<str>","m":"<str>","mc":"<str>",
 *               "fw":"<str>","fo":"<str>","bd":"<str>",
 *               "gh":"<str>","gb":"<str>","gbn":"<str>",
 *               "go":"<str>","dy":<bool>,
 *               "hw":<u32>,"hwt":<u32>,"hwb":<u32>,
 *               "hwc":<u32>,"hwcn":<u32>,"hwd":<u32>,
 *               "hwr":<u32>,"hwrn":"<str>","hwts":<u32>,
 *               "uid":"<16 hex chars>","bm":"<12 hex chars>",
 *               "fcc":"<str>","ic":"<str>","mic":"<str>",
 *               "srrc":"<str>","ncc":"<str>"}}
 *
 * Resources required: none.
 */

#pragma once

#include <stdint.h>
#include <stddef.h>

/**
 * Handle a "device_info" request.
 *
 * @param id     Request ID from the JSON envelope.
 * @param json   Full JSON line (unused — no arguments).
 * @param offset Byte offset past the already-parsed envelope fields.
 */
void device_info_handler(uint32_t id, const char* json, size_t offset);
