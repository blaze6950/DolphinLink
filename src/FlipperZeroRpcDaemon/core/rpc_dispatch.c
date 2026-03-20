/**
 * rpc_dispatch.c — RPC command registry and dispatcher implementation
 */

#include "rpc_dispatch.h"
/* Core */
#include "../handlers/core/ping.h"
#include "../handlers/core/stream_close.h"
/* System */
#include "../handlers/system/daemon_info.h"
#include "../handlers/system/daemon_stop.h"
#include "../handlers/system/device_info.h"
#include "../handlers/system/power_info.h"
#include "../handlers/system/datetime_get.h"
#include "../handlers/system/datetime_set.h"
#include "../handlers/system/region_info.h"
#include "../handlers/system/frequency_is_allowed.h"
#include "../handlers/system/reboot.h"
/* GPIO */
#include "../handlers/gpio/gpio_read.h"
#include "../handlers/gpio/gpio_write.h"
#include "../handlers/gpio/adc_read.h"
#include "../handlers/gpio/gpio_set_5v.h"
#include "../handlers/gpio/gpio_watch_start.h"
/* IR */
#include "../handlers/ir/ir_tx.h"
#include "../handlers/ir/ir_tx_raw.h"
#include "../handlers/ir/ir_receive_start.h"
/* Sub-GHz */
#include "../handlers/subghz/subghz_tx.h"
#include "../handlers/subghz/subghz_get_rssi.h"
#include "../handlers/subghz/subghz_rx_start.h"
/* NFC */
#include "../handlers/nfc/nfc_scan_start.h"
/* Notification */
#include "../handlers/notification/led_set.h"
#include "../handlers/notification/led_set_rgb.h"
#include "../handlers/notification/vibro.h"
#include "../handlers/notification/speaker_start.h"
#include "../handlers/notification/speaker_stop.h"
#include "../handlers/notification/backlight.h"
/* Storage */
#include "../handlers/storage/storage_info.h"
#include "../handlers/storage/storage_list.h"
#include "../handlers/storage/storage_read.h"
#include "../handlers/storage/storage_write.h"
#include "../handlers/storage/storage_mkdir.h"
#include "../handlers/storage/storage_remove.h"
#include "../handlers/storage/storage_stat.h"
/* RFID */
#include "../handlers/rfid/lfrfid_read_start.h"
/* iButton */
#include "../handlers/ibutton/ibutton_read_start.h"
/* Input */
#include "../handlers/input/input_listen_start.h"
/* UI canvas */
#include "../handlers/ui/ui_screen_acquire.h"
#include "../handlers/ui/ui_screen_release.h"
#include "../handlers/ui/ui_draw_str.h"
#include "../handlers/ui/ui_draw_rect.h"
#include "../handlers/ui/ui_draw_line.h"
#include "../handlers/ui/ui_flush.h"
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
 * Order does not matter — append new commands at the end.
 * ------------------------------------------------------------------------- */

static const RpcCommand commands[] = {
    /* ---- Core ---- */
    {"ping",                  0,                ping_handler},
    {"stream_close",          0,                stream_close_handler},

    /* ---- System / device info ---- */
    {"daemon_info",           0,                daemon_info_handler},
    {"daemon_stop",           0,                daemon_stop_handler},
    {"device_info",           0,                device_info_handler},
    {"power_info",            0,                power_info_handler},
    {"datetime_get",          0,                datetime_get_handler},
    {"datetime_set",          0,                datetime_set_handler},
    {"region_info",           0,                region_info_handler},
    {"frequency_is_allowed",  0,                frequency_is_allowed_handler},
    {"reboot",                0,                reboot_handler},

    /* ---- GPIO ---- */
    {"gpio_read",             0,                gpio_read_handler},
    {"gpio_write",            0,                gpio_write_handler},
    {"adc_read",              0,                adc_read_handler},
    {"gpio_set_5v",           0,                gpio_set_5v_handler},
    {"gpio_watch_start",      0,                gpio_watch_start_handler},

    /* ---- Infrared ---- */
    {"ir_tx",                 RESOURCE_IR,      ir_tx_handler},
    {"ir_tx_raw",             RESOURCE_IR,      ir_tx_raw_handler},
    {"ir_receive_start",      RESOURCE_IR,      ir_receive_start_handler},

    /* ---- Sub-GHz ---- */
    {"subghz_tx",             RESOURCE_SUBGHZ,  subghz_tx_handler},
    {"subghz_get_rssi",       RESOURCE_SUBGHZ,  subghz_get_rssi_handler},
    {"subghz_rx_start",       RESOURCE_SUBGHZ,  subghz_rx_start_handler},

    /* ---- NFC ---- */
    {"nfc_scan_start",        RESOURCE_NFC,     nfc_scan_start_handler},

    /* ---- Notifications / LED / vibro / speaker ---- */
    {"led_set",               0,                led_set_handler},
    {"led_set_rgb",           0,                led_set_rgb_handler},
    {"vibro",                 0,                vibro_handler},
    {"speaker_start",         RESOURCE_SPEAKER, speaker_start_handler},
    {"speaker_stop",          0,                speaker_stop_handler},
    {"backlight",             0,                backlight_handler},

    /* ---- Storage ---- */
    {"storage_info",          0,                storage_info_handler},
    {"storage_list",          0,                storage_list_handler},
    {"storage_read",          0,                storage_read_handler},
    {"storage_write",         0,                storage_write_handler},
    {"storage_mkdir",         0,                storage_mkdir_handler},
    {"storage_remove",        0,                storage_remove_handler},
    {"storage_stat",          0,                storage_stat_handler},

    /* ---- LF RFID ---- */
    {"lfrfid_read_start",     RESOURCE_RFID,    lfrfid_read_start_handler},

    /* ---- iButton ---- */
    {"ibutton_read_start",    RESOURCE_IBUTTON, ibutton_read_start_handler},

    /* ---- Input ---- */
    {"input_listen_start",    0,                input_listen_start_handler},

    /* ---- UI canvas ---- */
    {"ui_screen_acquire",     RESOURCE_GUI,     ui_screen_acquire_handler},
    {"ui_screen_release",     0,                ui_screen_release_handler},
    {"ui_draw_str",           0,                ui_draw_str_handler},
    {"ui_draw_rect",          0,                ui_draw_rect_handler},
    {"ui_draw_line",          0,                ui_draw_line_handler},
    {"ui_flush",              0,                ui_flush_handler},

    /* Sentinel */
    {NULL, 0, NULL},
};

/* -------------------------------------------------------------------------
 * Dispatcher
 * ------------------------------------------------------------------------- */

void rpc_dispatch(const char* json) {
    uint32_t request_id = 0;
    char cmd[64] = {0};

    /* Extract "id" then "cmd" sequentially with a cursor for fewer scans */
    const char* cursor = json;
    json_extract_uint32_at(json, &cursor, "id", &request_id);

    if(!json_extract_string_at(json, &cursor, "cmd", cmd, sizeof(cmd))) {
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
