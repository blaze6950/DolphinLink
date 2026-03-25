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
 *   {"t":0,"i":N}
 *
 * Wire format (response — error):
 *   {"t":0,"i":N,"e":"missing_path"}   — "path" field absent
 *   {"t":0,"i":N,"e":"missing_data"}   — "data" field absent
 *   {"t":0,"i":N,"e":"out_of_memory"}  — heap allocation failed
 *   {"t":0,"i":N,"e":"open_failed"}    — file could not be opened for writing
 *   {"t":0,"i":N,"e":"write_failed"}  — write did not complete (disk full or I/O error)
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

#include "storage_common.h"

void storage_write_handler(uint32_t id, const char* json, size_t offset) {
    JsonValue val;
    char path[PATH_MAX_LEN] = {0};
    if(!json_find(json, "p", offset, &val)) {
        rpc_send_error(id, "missing_path", "storage_write");
        return;
    }
    json_value_string(&val, path, sizeof(path));
    offset = val.offset;

    /* Extract the base64 data field — it can be large; use a heap buffer */
    /* The raw JSON line is at most RX_LINE_MAX (1024) bytes so base64 is ≤1024 */
    char* b64 = malloc(1024);
    if(!b64) {
        rpc_send_error(id, "out_of_memory", "storage_write");
        return;
    }

    if(!json_find(json, "d", offset, &val)) {
        free(b64);
        rpc_send_error(id, "missing_data", "storage_write");
        return;
    }
    json_value_string(&val, b64, 1024);
    (void)offset;

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

    size_t written = storage_file_write(f, raw, decoded);
    storage_file_close(f);
    storage_file_free(f);
    free(raw);

    if(written != decoded) {
        rpc_send_error(id, "write_failed", "storage_write");
        return;
    }

    rpc_send_ok(id, "storage_write");
    FURI_LOG_I("RPC", "storage_write %s (%zu B)", path, decoded);
}
