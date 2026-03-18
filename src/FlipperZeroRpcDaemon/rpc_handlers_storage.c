/**
 * rpc_handlers_storage.c — Storage filesystem RPC handler implementations
 *
 * storage_info   — filesystem label, capacity, free space
 * storage_list   — directory listing (names + is_dir + size)
 * storage_read   — read a file; response data is Base64-encoded
 * storage_write  — write Base64-decoded data to a file (creates/overwrites)
 * storage_mkdir  — create a directory
 * storage_remove — remove a file or empty directory
 * storage_stat   — stat a path (size + is_dir flag)
 *
 * All use g_storage (Storage*) from rpc_globals.h, opened at app startup.
 *
 * JSON protocol:
 *   storage_info:   {"id":N,"cmd":"storage_info","path":"/int"}
 *   storage_list:   {"id":N,"cmd":"storage_list","path":"/int"}
 *   storage_read:   {"id":N,"cmd":"storage_read","path":"/int/foo.txt"}
 *   storage_write:  {"id":N,"cmd":"storage_write","path":"/int/foo.txt","data":"<base64>"}
 *   storage_mkdir:  {"id":N,"cmd":"storage_mkdir","path":"/int/mydir"}
 *   storage_remove: {"id":N,"cmd":"storage_remove","path":"/int/foo.txt"}
 *   storage_stat:   {"id":N,"cmd":"storage_stat","path":"/int/foo.txt"}
 *
 * Read buffer: up to 4096 bytes per call (Base64-encoded max ~5468 chars).
 * This fits comfortably in the 8 KB stack when using a heap allocation.
 */

#include "rpc_handlers_storage.h"
#include "rpc_globals.h"
#include "rpc_base64.h"
#include "rpc_response.h"
#include "rpc_json.h"
#include "rpc_cmd_log.h"

#include <furi.h>
#include <storage/storage.h>
#include <stdio.h>
#include <string.h>
#include <inttypes.h>

/* Maximum file read chunk per call */
#define STORAGE_READ_MAX 4096

/* Maximum path length accepted */
#define PATH_MAX_LEN 256

/* Maximum number of directory entries emitted in one listing response */
#define STORAGE_LIST_MAX 64

/* =========================================================
 * storage_info
 * ========================================================= */

void storage_info_handler(uint32_t id, const char* json) {
    char path[PATH_MAX_LEN] = {0};
    if(!json_extract_string(json, "path", path, sizeof(path))) {
        rpc_send_error(id, "missing_path", "storage_info");
        return;
    }

    uint64_t total_space = 0;
    uint64_t free_space = 0;
    FS_Error err = storage_common_fs_info(g_storage, path, &total_space, &free_space);
    if(err != FSE_OK) {
        rpc_send_error(id, "storage_error", "storage_info");
        return;
    }

    /* Emit sizes as uint32 kibibytes to avoid 64-bit formatting headaches */
    uint32_t total_kb = (uint32_t)(total_space / 1024);
    uint32_t free_kb = (uint32_t)(free_space / 1024);

    char resp[256];
    snprintf(
        resp,
        sizeof(resp),
        "{\"id\":%" PRIu32
        ",\"status\":\"ok\",\"data\":{\"path\":\"%s\",\"total_kb\":%" PRIu32
        ",\"free_kb\":%" PRIu32 "}}\n",
        id,
        path,
        total_kb,
        free_kb);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(
        log_entry,
        sizeof(log_entry),
        "#%" PRIu32 " storage_info %s",
        id,
        path);

    rpc_send_response(resp, log_entry);
}

/* =========================================================
 * storage_list
 * ========================================================= */

void storage_list_handler(uint32_t id, const char* json) {
    char path[PATH_MAX_LEN] = {0};
    if(!json_extract_string(json, "path", path, sizeof(path))) {
        rpc_send_error(id, "missing_path", "storage_list");
        return;
    }

    File* dir = storage_file_alloc(g_storage);
    if(!storage_dir_open(dir, path)) {
        storage_file_free(dir);
        rpc_send_error(id, "open_failed", "storage_list");
        return;
    }

    /* Build a JSON array of entries inline into a heap buffer */
    /* Each entry: {"name":"...","is_dir":false,"size":1234} max ~290 chars */
    /* 64 entries × 290 = ~18 560 — allocate on heap */
    size_t buf_size = STORAGE_LIST_MAX * 290 + 128;
    char* buf = malloc(buf_size);
    if(!buf) {
        storage_dir_close(dir);
        storage_file_free(dir);
        rpc_send_error(id, "out_of_memory", "storage_list");
        return;
    }

    FileInfo fi;
    char name[256];
    size_t offset = 0;
    size_t count = 0;

    /* Header */
    offset += snprintf(
        buf + offset,
        buf_size - offset,
        "{\"id\":%" PRIu32 ",\"status\":\"ok\",\"data\":{\"entries\":[",
        id);

    while(count < STORAGE_LIST_MAX &&
          storage_dir_read(dir, &fi, name, sizeof(name))) {
        if(count > 0 && offset < buf_size - 1) {
            buf[offset++] = ',';
        }
        bool is_dir = (fi.flags & FSF_DIRECTORY) != 0;
        offset += snprintf(
            buf + offset,
            buf_size - offset,
            "{\"name\":\"%s\",\"is_dir\":%s,\"size\":%" PRIu32 "}",
            name,
            is_dir ? "true" : "false",
            (uint32_t)fi.size);
        count++;
    }

    storage_dir_close(dir);
    storage_file_free(dir);

    if(offset < buf_size - 3) {
        buf[offset++] = ']';
        buf[offset++] = '}';
        buf[offset++] = '}';
        buf[offset++] = '\n';
        buf[offset] = '\0';
    }

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " storage_list %s", id, path);

    rpc_send_response(buf, log_entry);
    free(buf);
}

/* =========================================================
 * storage_read
 * ========================================================= */

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

    /* Response: {"id":N,"status":"ok","data":{"data":"<base64>"}} */
    size_t resp_size = b64_size + 128;
    char* resp = malloc(resp_size);
    if(!resp) {
        free(b64);
        rpc_send_error(id, "out_of_memory", "storage_read");
        return;
    }

    snprintf(
        resp,
        resp_size,
        "{\"id\":%" PRIu32 ",\"status\":\"ok\",\"data\":{\"data\":\"%s\"}}\n",
        id,
        b64);
    free(b64);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " storage_read %s (%zu B)", id, path, bytes_read);

    rpc_send_response(resp, log_entry);
    free(resp);
}

/* =========================================================
 * storage_write
 * ========================================================= */

void storage_write_handler(uint32_t id, const char* json) {
    char path[PATH_MAX_LEN] = {0};
    if(!json_extract_string(json, "path", path, sizeof(path))) {
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

    if(!json_extract_string(json, "data", b64, 1024)) {
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

/* =========================================================
 * storage_mkdir
 * ========================================================= */

void storage_mkdir_handler(uint32_t id, const char* json) {
    char path[PATH_MAX_LEN] = {0};
    if(!json_extract_string(json, "path", path, sizeof(path))) {
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

/* =========================================================
 * storage_remove
 * ========================================================= */

void storage_remove_handler(uint32_t id, const char* json) {
    char path[PATH_MAX_LEN] = {0};
    if(!json_extract_string(json, "path", path, sizeof(path))) {
        rpc_send_error(id, "missing_path", "storage_remove");
        return;
    }

    FS_Error err = storage_common_remove(g_storage, path);
    if(err != FSE_OK) {
        rpc_send_error(id, "remove_failed", "storage_remove");
        return;
    }

    rpc_send_ok(id, "storage_remove");
    FURI_LOG_I("RPC", "storage_remove %s", path);
}

/* =========================================================
 * storage_stat
 * ========================================================= */

void storage_stat_handler(uint32_t id, const char* json) {
    char path[PATH_MAX_LEN] = {0};
    if(!json_extract_string(json, "path", path, sizeof(path))) {
        rpc_send_error(id, "missing_path", "storage_stat");
        return;
    }

    FileInfo fi;
    FS_Error err = storage_common_stat(g_storage, path, &fi);
    if(err != FSE_OK) {
        rpc_send_error(id, "stat_failed", "storage_stat");
        return;
    }

    bool is_dir = (fi.flags & FSF_DIRECTORY) != 0;

    char resp[256];
    snprintf(
        resp,
        sizeof(resp),
        "{\"id\":%" PRIu32
        ",\"status\":\"ok\",\"data\":{\"size\":%" PRIu32 ",\"is_dir\":%s}}\n",
        id,
        (uint32_t)fi.size,
        is_dir ? "true" : "false");

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " storage_stat %s", id, path);

    rpc_send_response(resp, log_entry);
}
