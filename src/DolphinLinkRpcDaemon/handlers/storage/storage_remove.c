/**
 * storage_remove.c — RPC handler implementation for the "storage_remove" command
 *
 * Removes a file or empty directory using storage_common_remove().
 *
 * Wire format (request):
 *   {"c":N,"i":N,"p":"/int/foo.txt"}
 *
 * Wire format (response — success):
 *   {"t":0,"i":N}
 *
 * Wire format (response — error):
 *   {"t":0,"i":N,"e":"missing_path"}   — "path" field absent
 *   {"t":0,"i":N,"e":"remove_failed"}  — storage_common_remove returned non-OK
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

#include "storage_common.h"

void storage_remove_handler(uint32_t id, const char* json, size_t offset) {
    JsonValue val;
    char path[PATH_MAX_LEN] = {0};
    if(!json_find(json, "p", offset, &val)) {
        rpc_send_error(id, "missing_path", "storage_remove");
        return;
    }
    json_value_string(&val, path, sizeof(path));
    (void)offset;

    FS_Error err = storage_common_remove(g_storage, path);
    if(err != FSE_OK) {
        rpc_send_error(id, "remove_failed", "storage_remove");
        return;
    }

    rpc_send_ok(id, "storage_remove");
    FURI_LOG_I("RPC", "storage_remove %s", path);
}
