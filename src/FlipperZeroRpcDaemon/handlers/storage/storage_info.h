/**
 * storage_info.h — RPC handler declaration for the "storage_info" command
 *
 * Returns filesystem label, total capacity, and free space for a given mount
 * point (e.g. "/int", "/ext", "/any").
 *
 * Wire format (request):
 *   {"id":N,"cmd":"storage_info","path":"/int"}
 *
 * Wire format (response — success):
 *   {"id":N,"status":"ok","data":{"path":"/int","total_kb":NNN,"free_kb":NNN}}
 *
 * Wire format (response — error):
 *   {"id":N,"error":"missing_path"}    — "path" field absent
 *   {"id":N,"error":"storage_error"}   — storage API returned non-OK status
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
