/**
 * storage_remove.h — RPC handler declaration for the "storage_remove" command
 *
 * Removes a file or empty directory using storage_common_remove().
 *
 * Wire format (request):
 *   {"c":35,"i":N,"p":"/int/foo.txt"}
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
#include <stddef.h>

/**
 * Handle the "storage_remove" RPC command.
 *
 * @param id     Request ID from the JSON message.
 * @param json   Full JSON line (null-terminated) received from the host.
 * @param offset Byte offset past the already-parsed "c" and "i" fields.
 */
void storage_remove_handler(uint32_t id, const char* json, size_t offset);
