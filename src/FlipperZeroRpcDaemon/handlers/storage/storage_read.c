/**
 * storage_read.c — RPC handler implementation for the "storage_read" command
 *
 * Reads up to STORAGE_READ_MAX (4096) bytes from a file and returns the
 * content Base64-encoded.  All buffers are heap-allocated to avoid stack
 * overflow on the 8 KB stack.
 *
 * Wire format (request):
 *   {"id":N,"cmd":"storage_read","path":"/int/foo.txt"}
 *
 * Wire format (response — success):
 *   {"type":"response","id":N,"payload":{"data":"<base64>"}}
 *
 * Wire format (response — error):
 *   {"id":N,"error":"missing_path"}   — "path" field absent
 *   {"id":N,"error":"open_failed"}    — file could not be opened
 *   {"id":N,"error":"out_of_memory"}  — heap allocation failed
 *
 * Resources: none (0).
 * Thread: main (FuriEventLoop).
 */

#include "storage_read.h"
#include "../../core/rpc_globals.h"
#include "../../core/rpc_base64.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"
#include "../../core/rpc_cmd_log.h"

#include <furi.h>
#include <storage/storage.h>
#include <stdio.h>
#include <stdlib.h>
#include <inttypes.h>

/** Maximum file read chunk per call. */
#define STORAGE_READ_MAX 4096
#define PATH_MAX_LEN     256

void storage_read_handler(uint32_t id, const char* json) {
    char path[PATH_MAX_LEN] = {0};
    if(!json_extract_string(json, "path", path, sizeof(path))) {
        rpc_send_error(id, "missing_path", "storage_read");
        return;
    }

    File* f = storage_file_alloc(g_storage);
    if(!storage_file_open(f, path, FSAM_READ, FSOM_OPEN_EXISTING)) {
        storage_file_free(f);
        rpc_send_error(id, "open_failed", "storage_read");
        return;
    }

    uint8_t* raw = malloc(STORAGE_READ_MAX);
    if(!raw) {
        storage_file_close(f);
        storage_file_free(f);
        rpc_send_error(id, "out_of_memory", "storage_read");
        return;
    }

    size_t bytes_read = storage_file_read(f, raw, STORAGE_READ_MAX);
    storage_file_close(f);
    storage_file_free(f);

    size_t b64_size = BASE64_ENCODED_SIZE(bytes_read);
    char* b64 = malloc(b64_size);
    if(!b64) {
        free(raw);
        rpc_send_error(id, "out_of_memory", "storage_read");
        return;
    }

    base64_encode(raw, bytes_read, b64, b64_size);
    free(raw);

    /* Payload: {"data":"<base64>"} */
    size_t resp_size = b64_size + 12; /* {"data":""} + b64 content */
    char* resp = malloc(resp_size);
    if(!resp) {
        free(b64);
        rpc_send_error(id, "out_of_memory", "storage_read");
        return;
    }

    snprintf(resp, resp_size, "{\"data\":\"%s\"}", b64);
    free(b64);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(
        log_entry,
        sizeof(log_entry),
        "#%" PRIu32 " storage_read %.10s (%zuB)",
        id,
        path,
        bytes_read);

    rpc_send_data_response(id, resp, log_entry);
    free(resp);
}
