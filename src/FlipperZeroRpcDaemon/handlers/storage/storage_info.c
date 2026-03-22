/**
 * storage_info.c — RPC handler implementation for the "storage_info" command
 *
 * Queries a filesystem mount point (e.g. "/int", "/ext") for its total
 * capacity and free space via storage_common_fs_info(), then responds with
 * both values expressed in kibibytes (KiB) to avoid 64-bit JSON formatting.
 *
 * Wire format (request):
 *   {"c":N,"i":N,"p":"/int"}
 *
 * Wire format (response — success):
 *   {"t":0,"i":N,"p":{"p":"/int","tk":NNN,"fk":NNN}}
 *
 * Wire format (response — error):
 *   {"id":N,"error":"missing_path"}   — "path" field absent
 *   {"id":N,"error":"storage_error"}  — storage API returned non-OK status
 *
 * Resources: none (0).
 * Thread: main (FuriEventLoop).
 */

#include "storage_info.h"
#include "../../core/rpc_globals.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"
#include "../../core/rpc_cmd_log.h"

#include <furi.h>
#include <storage/storage.h>
#include <stdio.h>
#include <inttypes.h>

#define PATH_MAX_LEN 256

void storage_info_handler(uint32_t id, const char* json) {
    char path[PATH_MAX_LEN] = {0};
    if(!json_extract_string(json, "p", path, sizeof(path))) {
        rpc_send_error(id, "missing_path", "storage_info");
        return;
    }

    uint64_t total_space = 0;
    uint64_t free_space = 0;
    FS_Error err = storage_common_fs_info(g_storage, path, &total_space, &free_space);
    if(err != FSE_OK) {
        rpc_send_error(id, "storage_error", "storage_info");
        return;
    }

    /* Emit sizes as uint32 kibibytes to avoid 64-bit formatting headaches */
    uint32_t total_kb = (uint32_t)(total_space / 1024);
    uint32_t free_kb = (uint32_t)(free_space / 1024);

    char resp[512];
    snprintf(
        resp,
        sizeof(resp),
        "{\"p\":\"%s\",\"tk\":%" PRIu32 ",\"fk\":%" PRIu32 "}",
        path,
        total_kb,
        free_kb);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " storage_info %.12s", id, path);

    rpc_send_data_response(id, resp, log_entry);
}
