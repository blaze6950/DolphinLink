/**
 * storage_list.c — RPC handler implementation for the "storage_list" command
 *
 * Opens a directory and reads up to STORAGE_LIST_MAX (64) entries.  Each entry
 * is serialised as {"name":"...","is_dir":bool,"size":N} into a heap buffer
 * (each entry can be up to ~290 bytes; 64 × 290 ≈ 18 560 bytes).
 *
 * Wire format (request):
 *   {"c":31,"i":N,"p":"/int"}
 *
 * Wire format (response — success):
 *   {"t":0,"i":N,"p":{"en":[...]}}
 *
 * Wire format (response — error):
 *   {"t":0,"i":N,"e":"missing_path"}   — "path" field absent
 *   {"t":0,"i":N,"e":"open_failed"}    — directory could not be opened
 *   {"t":0,"i":N,"e":"out_of_memory"}  — heap allocation failed
 *
 * Resources: none (0).
 * Thread: main (FuriEventLoop).
 */

#include "storage_list.h"
#include "../../core/rpc_globals.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"
#include "../../core/rpc_cmd_log.h"

#include <furi.h>
#include <storage/storage.h>
#include <stdio.h>
#include <stdlib.h>
#include <stdbool.h>
#include <inttypes.h>

#define PATH_MAX_LEN     256
#define STORAGE_LIST_MAX 64

void storage_list_handler(uint32_t id, const char* json) {
    char path[PATH_MAX_LEN] = {0};
    if(!json_extract_string(json, "p", path, sizeof(path))) {
        rpc_send_error(id, "missing_path", "storage_list");
        return;
    }

    File* dir = storage_file_alloc(g_storage);
    if(!storage_dir_open(dir, path)) {
        storage_file_free(dir);
        rpc_send_error(id, "open_failed", "storage_list");
        return;
    }

    /* Build payload: {"entries":[...]} in a heap buffer */
    /* Each entry: {"name":"...","is_dir":false,"size":1234} max ~290 chars */
    /* 64 entries × 290 = ~18 560 — allocate on heap */
    size_t buf_size = STORAGE_LIST_MAX * 290 + 128;
    char* buf = malloc(buf_size);
    if(!buf) {
        storage_dir_close(dir);
        storage_file_free(dir);
        rpc_send_error(id, "out_of_memory", "storage_list");
        return;
    }

    FileInfo fi;
    char name[256];
    size_t offset = 0;
    size_t count = 0;

    /* Payload header */
    offset += snprintf(buf + offset, buf_size - offset, "{\"en\":[");

    while(count < STORAGE_LIST_MAX && storage_dir_read(dir, &fi, name, sizeof(name))) {
        if(count > 0 && offset < buf_size - 1) {
            buf[offset++] = ',';
        }
        bool is_dir = (fi.flags & FSF_DIRECTORY) != 0;
        offset += snprintf(
            buf + offset,
            buf_size - offset,
            "{\"nm\":\"%s\",\"d\":%u,\"sz\":%" PRIu32 "}",
            name,
            is_dir ? 1u : 0u,
            (uint32_t)fi.size);
        count++;
    }

    storage_dir_close(dir);
    storage_file_free(dir);

    if(offset < buf_size - 2) {
        buf[offset++] = ']';
        buf[offset++] = '}';
        buf[offset] = '\0';
    }

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " storage_list %.12s", id, path);

    rpc_send_data_response(id, buf, log_entry);
    free(buf);
}
