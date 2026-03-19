/**
 * storage_write.h — RPC handler declaration for the "storage_write" command
 *
 * Decodes a Base64-encoded "data" field from the request and writes the
 * decoded bytes to a file, creating or overwriting it.
 *
 * Wire format (request):
 *   {"id":N,"cmd":"storage_write","path":"/int/foo.txt","data":"<base64>"}
 *
 * Wire format (response — success):
 *   {"id":N,"status":"ok"}
 *
 * Wire format (response — error):
 *   {"id":N,"error":"missing_path"}    — "path" field absent
 *   {"id":N,"error":"missing_data"}    — "data" field absent
 *   {"id":N,"error":"out_of_memory"}   — heap allocation failed
 *   {"id":N,"error":"open_failed"}     — file could not be opened for writing
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
