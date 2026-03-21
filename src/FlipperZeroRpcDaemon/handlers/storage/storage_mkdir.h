/**
 * storage_mkdir.h — RPC handler declaration for the "storage_mkdir" command
 *
 * Creates a directory at the specified path using storage_simply_mkdir().
 *
 * Wire format (request):
 *   {"id":N,"cmd":"storage_mkdir","path":"/int/mydir"}
 *
 * Wire format (response — success):
 *   {"t":0,"i":N}
 *
 * Wire format (response — error):
 *   {"t":0,"i":N,"e":"missing_path"}    — "path" field absent
 *   {"t":0,"i":N,"e":"mkdir_failed"}    — directory creation failed
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
