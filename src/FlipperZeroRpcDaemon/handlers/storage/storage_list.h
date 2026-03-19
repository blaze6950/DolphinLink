/**
 * storage_list.h — RPC handler declaration for the "storage_list" command
 *
 * Returns a directory listing of up to STORAGE_LIST_MAX (64) entries,
 * each with name, is_dir flag, and size in bytes.
 *
 * Wire format (request):
 *   {"id":N,"cmd":"storage_list","path":"/int"}
 *
 * Wire format (response — success):
 *   {"id":N,"status":"ok","data":{"entries":[
 *     {"name":"foo.txt","is_dir":false,"size":1234},
 *     {"name":"mydir","is_dir":true,"size":0}
 *   ]}}
 *
 * Wire format (response — error):
 *   {"id":N,"error":"missing_path"}    — "path" field absent
 *   {"id":N,"error":"open_failed"}     — directory could not be opened
 *   {"id":N,"error":"out_of_memory"}   — heap allocation failed
 *
 * Resources: none (0).
 */

#pragma once

#include <stdint.h>

/**
 * Handle the "storage_list" RPC command.
 *
 * @param id   Request ID from the JSON message.
 * @param json Full JSON line (null-terminated) received from the host.
 */
void storage_list_handler(uint32_t id, const char* json);
