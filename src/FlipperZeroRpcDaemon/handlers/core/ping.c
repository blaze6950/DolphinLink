/**
 * ping.c — ping command handler implementation
 *
 * Wire protocol:
 *   Request:  {"c":0,"i":N}
 *   Response: {"t":0,"i":N,"p":{"pg":1}}
 *
 * Resources required: none.
 * Threading: main thread (FuriEventLoop).
 */

#include "ping.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_cmd_log.h"

#include <furi.h>
#include <stdio.h>
#include <inttypes.h>

void ping_handler(uint32_t id, const char* json, size_t offset) {
    (void)json;
    (void)offset;

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " ping -> ok", id);

    rpc_send_data_response(id, "{\"pg\":1}", log_entry);
}
