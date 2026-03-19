/**
 * storage_read.h — RPC handler declaration for the "storage_read" command
 *
 * Reads up to STORAGE_READ_MAX (4096) bytes from a file and returns the
 * content as a Base64-encoded string.
 *
 * Wire format (request):
 *   {"id":N,"cmd":"storage_read","path":"/int/foo.txt"}
 *
 * Wire format (response — success):
 *   {"id":N,"status":"ok","data":{"data":"<base64>"}}
 *
 * Wire format (response — error):
 *   {"id":N,"error":"missing_path"}    — "path" field absent
 *   {"id":N,"error":"open_failed"}     — file could not be opened
 *   {"id":N,"error":"out_of_memory"}   — heap allocation failed
 *
 * Resources: none (0).
 */

#pragma once

#include <stdint.h>

/**
 * Handle the "storage_read" RPC command.
 *
 * @param id   Request ID from the JSON message.
 * @param json Full JSON line (null-terminated) received from the host.
 */
void storage_read_handler(uint32_t id, const char* json);
