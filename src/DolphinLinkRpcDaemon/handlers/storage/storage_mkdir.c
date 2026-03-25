/**
 * storage_mkdir.c — RPC handler implementation for the "storage_mkdir" command
 *
 * Creates a directory at the specified path using storage_simply_mkdir().
 *
 * Wire format (request):
 *   {"c":34,"i":N,"p":"/int/mydir"}
 *
 * Wire format (response — success):
 *   {"t":0,"i":N}
 *
 * Wire format (response — error):
 *   {"t":0,"i":N,"e":"missing_path"}   — "path" field absent
 *   {"t":0,"i":N,"e":"mkdir_failed"}   — directory creation failed
 *
 * Resources: none (0).
 * Thread: main (FuriEventLoop).
 */

#include "storage_mkdir.h"
#include "../../core/rpc_globals.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"

#include <furi.h>
#include <storage/storage.h>
#include <inttypes.h>

#include "storage_common.h"

void storage_mkdir_handler(uint32_t id, const char* json, size_t offset) {
    JsonValue val;
    char path[PATH_MAX_LEN] = {0};
    if(!json_find(json, "p", offset, &val)) {
        rpc_send_error(id, "missing_path", "storage_mkdir");
        return;
    }
    json_value_string(&val, path, sizeof(path));
    (void)offset;

    if(!storage_simply_mkdir(g_storage, path)) {
        rpc_send_error(id, "mkdir_failed", "storage_mkdir");
        return;
    }

    rpc_send_ok(id, "storage_mkdir");
    FURI_LOG_I("RPC", "storage_mkdir %s", path);
}
