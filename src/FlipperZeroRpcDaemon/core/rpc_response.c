/**
 * rpc_response.c — RPC response formatting helpers implementation (Wire Format V3)
 */

#include "rpc_response.h"
#include "rpc_transport.h"
#include "rpc_cmd_log.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

void rpc_send_error(uint32_t id, const char* error_code, const char* cmd_name) {
    char buf[128];
    snprintf(
        buf,
        sizeof(buf),
        "{\"t\":0,\"i\":%" PRIu32 ",\"e\":\"%s\"}\n",
        id,
        error_code);
    cdc_send(buf);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(
        log_entry, sizeof(log_entry), "#%" PRIu32 " %.12s -> err:%.10s", id, cmd_name, error_code);
    cmd_log_push(log_entry);
}

void rpc_send_ok(uint32_t id, const char* cmd_name) {
    char buf[128];
    snprintf(buf, sizeof(buf), "{\"t\":0,\"i\":%" PRIu32 "}\n", id);
    cdc_send(buf);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " %.20s -> ok", id, cmd_name);
    cmd_log_push(log_entry);
}

void rpc_send_data_response(uint32_t id, const char* payload_json, const char* log_entry) {
    /* Header: {"t":0,"i":<id>,"p":  + payload + }\n
     * Max header overhead: ~20 bytes + PRIu32 (10 digits) = ~30 bytes */
    size_t payload_len = strlen(payload_json);
    size_t buf_size = payload_len + 64;

    /* Use stack buffer for small payloads; heap for large ones. */
    char stack_buf[256];
    char* buf = NULL;
    char* heap_buf = NULL;

    if(buf_size <= sizeof(stack_buf)) {
        buf = stack_buf;
    } else {
        heap_buf = malloc(buf_size);
        if(!heap_buf) {
            /* Out of memory — send a generic error instead of silently dropping */
            rpc_send_error(id, "out_of_memory", "rpc_send_data_response");
            return;
        }
        buf = heap_buf;
    }

    snprintf(
        buf,
        buf_size,
        "{\"t\":0,\"i\":%" PRIu32 ",\"p\":%s}\n",
        id,
        payload_json);

    cdc_send(buf);
    cmd_log_push(log_entry);

    if(heap_buf) free(heap_buf);
}
