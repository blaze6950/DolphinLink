/**
 * subghz_get_rssi.c — subghz_get_rssi RPC handler implementation
 *
 * Briefly enables the CC1101 receiver for ~5 ms to sample RSSI, then sleeps
 * the radio.  RSSI is returned as integer tenths-of-dBm.
 *
 * Wire format (request):
 *   {"c":N,"i":M,"fr":433920000}
 *
 * Wire format (response):
 *   {"t":0,"i":N,"p":{"rs":-750}}
 *
 * Resources: RESOURCE_SUBGHZ (dispatcher pre-checks; handler acquires and
 *            releases before returning)
 */

#include "subghz_get_rssi.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_resource.h"
#include "../../core/rpc_json.h"
#include "../../core/rpc_cmd_log.h"

#include <furi.h>
#include <furi_hal_subghz.h>
#include <stdio.h>
#include <inttypes.h>

void subghz_get_rssi_handler(uint32_t id, const char* json, size_t offset) {
    uint32_t freq = 433920000;
    JsonValue val;
    if(json_find(json, "fr", offset, &val)) { json_value_uint32(&val, &freq); }
    (void)offset;

    resource_acquire(RESOURCE_SUBGHZ);

    furi_hal_subghz_reset();
    furi_hal_subghz_set_frequency_and_path(freq);
    furi_hal_subghz_rx();
    furi_delay_ms(5); /* wait for AGC to settle */

    float rssi = furi_hal_subghz_get_rssi();

    furi_hal_subghz_sleep();
    resource_release(RESOURCE_SUBGHZ);

    /* Encode as integer tenths-of-dBm to avoid %f */
    int32_t rssi_10 = (int32_t)(rssi * 10.0f);

    char resp[40];
    snprintf(resp, sizeof(resp), "{\"rs\":%" PRIi32 "}", rssi_10);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " subghz_rssi -> %" PRIi32, id, rssi_10);

    rpc_send_data_response(id, resp, log_entry);
}
