/**
 * storage_stat.c — RPC handler implementation for the "storage_stat" command
 *
 * Stats a file or directory using storage_common_stat(), returning its size
 * in bytes and an is_dir flag.
 *
 * Wire format (request):
 *   {"id":N,"cmd":"storage_stat","path":"/int/foo.txt"}
 *
 * Wire format (response — success):
 *   {"type":"response","id":N,"payload":{"size":1234,"is_dir":false}}
 *
 * Wire format (response — error):
 *   {"id":N,"error":"missing_path"}  — "path" field absent
 *   {"id":N,"error":"stat_failed"}   — storage_common_stat returned non-OK
 *
 * Resources: none (0).
 * Thread: main (FuriEventLoop).
 */

#include "storage_stat.h"
#include "../../core/rpc_globals.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"
#include "../../core/rpc_cmd_log.h"

#include <furi.h>
#include <storage/storage.h>
#include <stdio.h>
#include <stdbool.h>
#include <inttypes.h>

#define PATH_MAX_LEN 256

void storage_stat_handler(uint32_t id, const char* json) {
    char path[PATH_MAX_LEN] = {0};
    if(!json_extract_string(json, "path", path, sizeof(path))) {
        rpc_send_error(id, "missing_path", "storage_stat");
        return;
    }

    FileInfo fi;
    FS_Error err = storage_common_stat(g_storage, path, &fi);
    if(err != FSE_OK) {
        rpc_send_error(id, "stat_failed", "storage_stat");
        return;
    }

    bool is_dir = (fi.flags & FSF_DIRECTORY) != 0;

    char resp[64];
    snprintf(
        resp,
        sizeof(resp),
        "{\"size\":%" PRIu32 ",\"is_dir\":%s}",
        (uint32_t)fi.size,
        is_dir ? "true" : "false");

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " storage_stat %.12s", id, path);

    rpc_send_data_response(id, resp, log_entry);
}
