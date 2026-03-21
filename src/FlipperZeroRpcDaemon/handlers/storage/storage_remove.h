/**
 * storage_remove.h — RPC handler declaration for the "storage_remove" command
 *
 * Removes a file or empty directory using storage_common_remove().
 *
 * Wire format (request):
 *   {"id":N,"cmd":"storage_remove","path":"/int/foo.txt"}
 *
 * Wire format (response — success):
 *   {"t":0,"i":N}
 *
 * Wire format (response — error):
 *   {"t":0,"i":N,"e":"missing_path"}    — "path" field absent
 *   {"t":0,"i":N,"e":"remove_failed"}   — removal returned non-OK status
 *
 * Resources: none (0).
 */

#pragma once

#include <stdint.h>

/**
 * Handle the "storage_remove" RPC command.
 *
 * @param id   Request ID from the JSON message.
 * @param json Full JSON line (null-terminated) received from the host.
 */
void storage_remove_handler(uint32_t id, const char* json);
