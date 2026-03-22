/**
 * storage_list.h — RPC handler declaration for the "storage_list" command
 *
 * Returns a directory listing of up to STORAGE_LIST_MAX (64) entries,
 * each with name, is_dir flag, and size in bytes.
 *
 * Wire format (request):
 *   {"c":31,"i":N,"p":"/int"}
 *
 * Wire format (response — success):
 *   {"t":0,"i":N,"p":{"e":[
 *     {"name":"foo.txt","is_dir":false,"size":1234},
 *     {"name":"mydir","is_dir":true,"size":0}
 *   ]}}
 *
 * Wire format (response — error):
 *   {"t":0,"i":N,"e":"missing_path"}    — "path" field absent
 *   {"t":0,"i":N,"e":"open_failed"}     — directory could not be opened
 *   {"t":0,"i":N,"e":"out_of_memory"}   — heap allocation failed
 *
 * Resources: none (0).
 */

#pragma once

#include <stdint.h>
#include <stddef.h>

/**
 * Handle the "storage_list" RPC command.
 *
 * @param id     Request ID from the JSON message.
 * @param json   Full JSON line (null-terminated) received from the host.
 * @param offset Byte offset past the already-parsed "c" and "i" fields.
 */
void storage_list_handler(uint32_t id, const char* json, size_t offset);
