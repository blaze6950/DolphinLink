/**
 * storage_mkdir.h — RPC handler declaration for the "storage_mkdir" command
 *
 * Creates a directory at the specified path using storage_simply_mkdir().
 *
 * Wire format (request):
 *   {"id":N,"cmd":"storage_mkdir","path":"/int/mydir"}
 *
 * Wire format (response — success):
 *   {"id":N,"status":"ok"}
 *
 * Wire format (response — error):
 *   {"id":N,"error":"missing_path"}    — "path" field absent
 *   {"id":N,"error":"mkdir_failed"}    — directory creation failed
 *
 * Resources: none (0).
 */

#pragma once

#include <stdint.h>

/**
 * Handle the "storage_mkdir" RPC command.
 *
 * @param id   Request ID from the JSON message.
 * @param json Full JSON line (null-terminated) received from the host.
 */
void storage_mkdir_handler(uint32_t id, const char* json);
