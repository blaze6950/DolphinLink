/**
 * rpc_response.c — RPC response formatting helpers implementation
 */

#include "rpc_response.h"
#include "rpc_transport.h"
#include "rpc_cmd_log.h"

#include <stdio.h>

void rpc_send_error(uint32_t id, const char* error_code, const char* cmd_name) {
    char buf[128];
    snprintf(buf, sizeof(buf), "{\"id\":%" PRIu32 ",\"error\":\"%s\"}\n", id, error_code);
    cdc_send(buf);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(
        log_entry, sizeof(log_entry), "#%" PRIu32 " %.12s -> err:%.10s", id, cmd_name, error_code);
    cmd_log_push(log_entry);
}

void rpc_send_ok(uint32_t id, const char* cmd_name) {
    char buf[128];
    snprintf(buf, sizeof(buf), "{\"id\":%" PRIu32 ",\"status\":\"ok\"}\n", id);
    cdc_send(buf);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " %.20s -> ok", id, cmd_name);
    cmd_log_push(log_entry);
}

void rpc_send_response(const char* json_line, const char* log_entry) {
    cdc_send(json_line);
    cmd_log_push(log_entry);
}
