/**
 * region_info.c — region_info command handler implementation
 *
 * Wire protocol:
 *   Request:  {"c":9,"i":N}
 *   Response: {"t":0,"i":N,"p":{
 *               "rg":"<name>",
 *               "bands":[{"start":<u32>,"end":<u32>,"power_limit":<u8>},...] }}
 *
 * Reads the active regulatory region from furi_hal_region_get() and serialises
 * all allowed frequency bands into a compact JSON array (up to 512 bytes).
 *
 * Resources required: none.
 * Threading: main thread (FuriEventLoop).
 */

#include "region_info.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_cmd_log.h"

#include <furi.h>
#include <furi_hal_region.h>
#include <stdio.h>
#include <inttypes.h>

void region_info_handler(uint32_t id, const char* json, size_t offset) {
    (void)json;
    (void)offset;

    const FuriHalRegion* region = furi_hal_region_get();
    const char* region_name = furi_hal_region_get_name();
    if(!region_name) region_name = "unknown";

    /* Build a compact JSON array of allowed bands */
    char bands_buf[512];
    size_t pos = 0;
    bands_buf[pos++] = '[';

    if(region) {
        for(size_t i = 0; i < region->bands_count; i++) {
            if(i > 0 && pos < sizeof(bands_buf) - 1) bands_buf[pos++] = ',';
            int written = snprintf(
                bands_buf + pos,
                sizeof(bands_buf) - pos,
                "{\"start\":%" PRIu32 ",\"end\":%" PRIu32 ",\"power_limit\":%" PRIu8 "}",
                region->bands[i].start,
                region->bands[i].end,
                region->bands[i].power_limit);
            if(written > 0) pos += (size_t)written;
            if(pos >= sizeof(bands_buf) - 2) break;
        }
    }
    if(pos < sizeof(bands_buf) - 1) bands_buf[pos++] = ']';
    bands_buf[pos] = '\0';

    char resp[640];
    snprintf(
        resp,
        sizeof(resp),
        "{\"rg\":\"%s\",\"bands\":%s}",
        region_name,
        bands_buf);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " region_info -> ok", id);

    rpc_send_data_response(id, resp, log_entry);
}
