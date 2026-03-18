/**
 * rpc_dispatch.c — RPC command registry and dispatcher implementation
 */

#include "rpc_dispatch.h"
#include "rpc_handlers.h"
#include "rpc_json.h"
#include "rpc_response.h"
#include "rpc_resource.h"

#include <furi.h>
#include <string.h>

/* -------------------------------------------------------------------------
 * Command registry
 *
 * Null-terminated array of supported commands.  Add a new row here and
 * implement the corresponding handler in rpc_handlers.c to register a
 * new command.
 * ------------------------------------------------------------------------- */

static const RpcCommand commands[] = {
    {"ping", 0, ping_handler},
    {"ble_scan_start", RESOURCE_BLE, ble_scan_start_handler},
    {"stream_close", 0, stream_close_handler},
    {NULL, 0, NULL},
};

/* -------------------------------------------------------------------------
 * Dispatcher
 * ------------------------------------------------------------------------- */

void rpc_dispatch(const char* json) {
    uint32_t request_id = 0;
    char cmd[64] = {0};

    json_extract_uint32(json, "id", &request_id);

    if(!json_extract_string(json, "cmd", cmd, sizeof(cmd))) {
        rpc_send_error(request_id, "missing_cmd", "???");
        return;
    }

    FURI_LOG_I("RPC", "cmd=%s id=%" PRIu32, cmd, request_id);

    for(size_t i = 0; commands[i].name != NULL; i++) {
        if(strcmp(commands[i].name, cmd) == 0) {
            if(commands[i].resources && !resource_can_acquire(commands[i].resources)) {
                rpc_send_error(request_id, "resource_busy", cmd);
                return;
            }
            commands[i].handler(request_id, json);
            return;
        }
    }

    rpc_send_error(request_id, "unknown_command", cmd);
}
