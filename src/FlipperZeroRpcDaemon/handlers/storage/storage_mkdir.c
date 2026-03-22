/**
 * storage_mkdir.c — RPC handler implementation for the "storage_mkdir" command
 *
 * Creates a directory at the specified path using storage_simply_mkdir().
 *
 * Wire format (request):
 *   {"id":N,"cmd":"storage_mkdir","path":"/int/mydir"}
 *
 * Wire format (response — success):
 *   {"id":N,"status":"ok"}
 *
 * Wire format (response — error):
 *   {"id":N,"error":"missing_path"}   — "path" field absent
 *   {"id":N,"error":"mkdir_failed"}   — directory creation failed
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

#define PATH_MAX_LEN 256

void storage_mkdir_handler(uint32_t id, const char* json) {
    char path[PATH_MAX_LEN] = {0};
    if(!json_extract_string(json, "p", path, sizeof(path))) {
        rpc_send_error(id, "missing_path", "storage_mkdir");
        return;
    }

    if(!storage_simply_mkdir(g_storage, path)) {
        rpc_send_error(id, "mkdir_failed", "storage_mkdir");
        return;
    }

    rpc_send_ok(id, "storage_mkdir");
    FURI_LOG_I("RPC", "storage_mkdir %s", path);
}
