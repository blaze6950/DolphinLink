/**
 * storage_remove.c — RPC handler implementation for the "storage_remove" command
 *
 * Removes a file or empty directory using storage_common_remove().
 *
 * Wire format (request):
 *   {"id":N,"cmd":"storage_remove","path":"/int/foo.txt"}
 *
 * Wire format (response — success):
 *   {"id":N,"status":"ok"}
 *
 * Wire format (response — error):
 *   {"id":N,"error":"missing_path"}   — "path" field absent
 *   {"id":N,"error":"remove_failed"}  — storage_common_remove returned non-OK
 *
 * Resources: none (0).
 * Thread: main (FuriEventLoop).
 */

#include "storage_remove.h"
#include "../../core/rpc_globals.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"

#include <furi.h>
#include <storage/storage.h>
#include <inttypes.h>

#define PATH_MAX_LEN 256

void storage_remove_handler(uint32_t id, const char* json) {
    char path[PATH_MAX_LEN] = {0};
    if(!json_extract_string(json, "path", path, sizeof(path))) {
        rpc_send_error(id, "missing_path", "storage_remove");
        return;
    }

    FS_Error err = storage_common_remove(g_storage, path);
    if(err != FSE_OK) {
        rpc_send_error(id, "remove_failed", "storage_remove");
        return;
    }

    rpc_send_ok(id, "storage_remove");
    FURI_LOG_I("RPC", "storage_remove %s", path);
}
