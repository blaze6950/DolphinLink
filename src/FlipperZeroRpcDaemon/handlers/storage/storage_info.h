/**
 * storage_info.h — RPC handler declaration for the "storage_info" command
 *
 * Returns filesystem label, total capacity, and free space for a given mount
 * point (e.g. "/int", "/ext", "/any").
 *
 * Wire format (request):
 *   {"c":30,"i":N,"p":"/int"}
 *
 * Wire format (response — success):
 *   {"t":0,"i":N,"p":{"p":"/int","tk":NNN,"fk":NNN}}
 *
 * Wire format (response — error):
 *   {"t":0,"i":N,"e":"missing_path"}    — "path" field absent
 *   {"t":0,"i":N,"e":"storage_error"}   — storage API returned non-OK status
 *
 * Resources: none (0).
 */

#pragma once

#include <stdint.h>

/**
 * Handle the "storage_info" RPC command.
 *
 * @param id   Request ID from the JSON message.
 * @param json Full JSON line (null-terminated) received from the host.
 */
void storage_info_handler(uint32_t id, const char* json);
