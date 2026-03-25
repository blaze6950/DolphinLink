/**
 * storage_stat.c — RPC handler implementation for the "storage_stat" command
 *
 * Stats a file or directory using storage_common_stat(), returning its size
 * in bytes and an is_dir flag.
 *
 * Wire format (request):
 *   {"c":N,"i":M,"p":"/int/foo.txt"}
 *
 * Wire format (response — success):
 *   {"t":0,"i":N,"p":{"sz":1234,"d":0}}
 *
 * Wire format (response — error):
 *   {"t":0,"i":N,"e":"missing_path"}  — "path" field absent
 *   {"t":0,"i":N,"e":"stat_failed"}   — storage_common_stat returned non-OK
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

#include "storage_common.h"

void storage_stat_handler(uint32_t id, const char* json, size_t offset) {
    JsonValue val;
    char path[PATH_MAX_LEN] = {0};
    if(!json_find(json, "p", offset, &val)) {
        rpc_send_error(id, "missing_path", "storage_stat");
        return;
    }
    json_value_string(&val, path, sizeof(path));
    (void)offset;

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
        "{\"sz\":%" PRIu32 ",\"d\":%u}",
        (uint32_t)fi.size,
        is_dir ? 1u : 0u);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " storage_stat %.12s", id, path);

    rpc_send_data_response(id, resp, log_entry);
}
