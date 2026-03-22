/**
 * storage_write.h — RPC handler declaration for the "storage_write" command
 *
 * Decodes a Base64-encoded "data" field from the request and writes the
 * decoded bytes to a file, creating or overwriting it.
 *
 * Wire format (request):
 *   {"c":33,"i":N,"p":"/int/foo.txt","d":"<base64>"}
 *
 * Wire format (response — success):
 *   {"t":0,"i":N}
 *
 * Wire format (response — error):
 *   {"t":0,"i":N,"e":"missing_path"}    — "path" field absent
 *   {"t":0,"i":N,"e":"missing_data"}    — "data" field absent
 *   {"t":0,"i":N,"e":"out_of_memory"}   — heap allocation failed
 *   {"t":0,"i":N,"e":"open_failed"}     — file could not be opened for writing
 *
 * Resources: none (0).
 */

#pragma once

#include <stdint.h>

/**
 * Handle the "storage_write" RPC command.
 *
 * @param id   Request ID from the JSON message.
 * @param json Full JSON line (null-terminated) received from the host.
 */
void storage_write_handler(uint32_t id, const char* json);
