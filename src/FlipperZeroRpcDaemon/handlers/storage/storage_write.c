/**
 * storage_write.c — RPC handler implementation for the "storage_write" command
 *
 * Decodes a Base64-encoded "data" field and writes the raw bytes to a file,
 * creating or overwriting it.  The raw JSON line is at most RX_LINE_MAX
 * (1 024) bytes so the Base64 value is at most 1 024 characters.
 *
 * Wire format (request):
 *   {"c":N,"i":M,"p":"/int/foo.txt","d":"<base64>"}
 *
 * Wire format (response — success):
 *   {"id":N,"status":"ok"}
 *
 * Wire format (response — error):
 *   {"id":N,"error":"missing_path"}   — "path" field absent
 *   {"id":N,"error":"missing_data"}   — "data" field absent
 *   {"id":N,"error":"out_of_memory"}  — heap allocation failed
 *   {"id":N,"error":"open_failed"}    — file could not be opened for writing
 *
 * Resources: none (0).
 * Thread: main (FuriEventLoop).
 */

#include "storage_write.h"
#include "../../core/rpc_globals.h"
#include "../../core/rpc_base64.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"

#include <furi.h>
#include <storage/storage.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <inttypes.h>

#define PATH_MAX_LEN 256

void storage_write_handler(uint32_t id, const char* json) {
    char path[PATH_MAX_LEN] = {0};
    const char* cursor = json;
    if(!json_extract_string_at(json, &cursor, "p", path, sizeof(path))) {
        rpc_send_error(id, "missing_path", "storage_write");
        return;
    }

    /* Extract the base64 data field — it can be large; use a heap buffer */
    /* The raw JSON line is at most RX_LINE_MAX (1024) bytes so base64 is ≤1024 */
    char* b64 = malloc(1024);
    if(!b64) {
        rpc_send_error(id, "out_of_memory", "storage_write");
        return;
    }

    if(!json_extract_string_at(json, &cursor, "d", b64, 1024)) {
        free(b64);
        rpc_send_error(id, "missing_data", "storage_write");
        return;
    }

    size_t decode_size = BASE64_DECODED_SIZE(strlen(b64));
    uint8_t* raw = malloc(decode_size);
    if(!raw) {
        free(b64);
        rpc_send_error(id, "out_of_memory", "storage_write");
        return;
    }

    size_t decoded = base64_decode(b64, raw, decode_size);
    free(b64);

    File* f = storage_file_alloc(g_storage);
    if(!storage_file_open(f, path, FSAM_WRITE, FSOM_CREATE_ALWAYS)) {
        free(raw);
        storage_file_free(f);
        rpc_send_error(id, "open_failed", "storage_write");
        return;
    }

    storage_file_write(f, raw, decoded);
    storage_file_close(f);
    storage_file_free(f);
    free(raw);

    rpc_send_ok(id, "storage_write");
    FURI_LOG_I("RPC", "storage_write %s (%zu B)", path, decoded);
}
