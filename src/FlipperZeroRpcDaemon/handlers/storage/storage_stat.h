/**
 * storage_stat.h — RPC handler declaration for the "storage_stat" command
 *
 * Stats a file or directory, returning its size and whether it is a directory.
 *
 * Wire format (request):
 *   {"id":N,"cmd":"storage_stat","path":"/int/foo.txt"}
 *
 * Wire format (response — success):
 *   {"id":N,"status":"ok","data":{"size":1234,"is_dir":false}}
 *
 * Wire format (response — error):
 *   {"id":N,"error":"missing_path"}  — "path" field absent
 *   {"id":N,"error":"stat_failed"}   — stat returned non-OK status
 *
 * Resources: none (0).
 */

#pragma once

#include <stdint.h>

/**
 * Handle the "storage_stat" RPC command.
 *
 * @param id   Request ID from the JSON message.
 * @param json Full JSON line (null-terminated) received from the host.
 */
void storage_stat_handler(uint32_t id, const char* json);
