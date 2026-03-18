/**
 * rpc_dispatch.c — RPC command registry and dispatcher implementation
 */

#include "rpc_dispatch.h"
#include "../handlers/rpc_handlers.h"
#include "../handlers/rpc_handlers_system.h"
#include "../handlers/rpc_handlers_gpio.h"
#include "../handlers/rpc_handlers_ir.h"
#include "../handlers/rpc_handlers_subghz.h"
#include "../handlers/rpc_handlers_nfc.h"
#include "../handlers/rpc_handlers_notification.h"
#include "../handlers/rpc_handlers_storage.h"
#include "../handlers/rpc_handlers_rfid.h"
#include "../handlers/rpc_handlers_ibutton.h"
#include "rpc_json.h"
#include "rpc_response.h"
#include "rpc_resource.h"

#include <furi.h>
#include <string.h>

/* -------------------------------------------------------------------------
 * Command registry
 *
 * Null-terminated array of supported commands.  Each row:
 *   { "command_name", RESOURCE_FLAGS, handler_function }
 *
 * RESOURCE_FLAGS == 0 means no exclusive resource is needed.
 * The dispatcher checks resource_can_acquire() before calling the handler.
 * ------------------------------------------------------------------------- */

static const RpcCommand commands[] = {
    /* ---- Core ---- */
    {"ping", 0, ping_handler},
    {"stream_close", 0, stream_close_handler},

    /* ---- System / device info ---- */
    {"device_info", 0, device_info_handler},
    {"power_info", 0, power_info_handler},
    {"datetime_get", 0, datetime_get_handler},
    {"datetime_set", 0, datetime_set_handler},
    {"region_info", 0, region_info_handler},
    {"frequency_is_allowed", 0, frequency_is_allowed_handler},

    /* ---- GPIO ---- */
    {"gpio_read", 0, gpio_read_handler},
    {"gpio_write", 0, gpio_write_handler},
    {"adc_read", 0, adc_read_handler},
    {"gpio_set_5v", 0, gpio_set_5v_handler},
    {"gpio_watch_start", 0, gpio_watch_start_handler},

    /* ---- Infrared ---- */
    {"ir_tx", RESOURCE_IR, ir_tx_handler},
    {"ir_tx_raw", RESOURCE_IR, ir_tx_raw_handler},
    {"ir_receive_start", RESOURCE_IR, ir_receive_start_handler},

    /* ---- Sub-GHz ---- */
    {"subghz_tx", RESOURCE_SUBGHZ, subghz_tx_handler},
    {"subghz_get_rssi", RESOURCE_SUBGHZ, subghz_get_rssi_handler},
    {"subghz_rx_start", RESOURCE_SUBGHZ, subghz_rx_start_handler},

    /* ---- NFC ---- */
    {"nfc_scan_start", RESOURCE_NFC, nfc_scan_start_handler},

    /* ---- Notifications / LED / vibro / speaker ---- */
    {"led_set", 0, led_set_handler},
    {"led_set_rgb", 0, led_set_rgb_handler},
    {"vibro", 0, vibro_handler},
    {"speaker_start", RESOURCE_SPEAKER, speaker_start_handler},
    {"speaker_stop", 0, speaker_stop_handler},
    {"backlight", 0, backlight_handler},

    /* ---- Storage ---- */
    {"storage_info", 0, storage_info_handler},
    {"storage_list", 0, storage_list_handler},
    {"storage_read", 0, storage_read_handler},
    {"storage_write", 0, storage_write_handler},
    {"storage_mkdir", 0, storage_mkdir_handler},
    {"storage_remove", 0, storage_remove_handler},
    {"storage_stat", 0, storage_stat_handler},

    /* ---- LF RFID ---- */
    {"lfrfid_read_start", RESOURCE_RFID, lfrfid_read_start_handler},

    /* ---- iButton ---- */
    {"ibutton_read_start", RESOURCE_IBUTTON, ibutton_read_start_handler},

    /* Sentinel */
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
