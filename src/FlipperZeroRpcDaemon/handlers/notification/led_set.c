/**
 * led_set.c — RPC handler implementation for the "led_set" command
 *
 * Sets a single RGB LED channel (red, green, or blue) to an intensity value
 * in the range 0–255 using furi_hal_light_set().
 *
 * Wire format (request):
 *   {"c":24,"i":N,"cl":0|1|2,"vl":0-255}
 *   cl: LedChannel integer — 0=Red, 1=Green, 2=Blue
 *   vl: brightness 0–255
 *
 * Wire format (response — success):
 *   {"t":0,"i":N}
 *
 * Wire format (response — error):
 *   "missing_color" — "cl" field absent
 *   "invalid_color" — value not 0, 1, or 2
 *
 * Resources: none (0).
 * Thread: main (FuriEventLoop).
 */

#include "led_set.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"

#include <furi.h>
#include <furi_hal_light.h>
#include <inttypes.h>

void led_set_handler(uint32_t id, const char* json) {
    uint32_t channel = 0;
    if(!json_extract_uint32(json, "cl", &channel)) {
        rpc_send_error(id, "missing_color", "led_set");
        return;
    }

    uint32_t value = 0;
    json_extract_uint32(json, "vl", &value);
    if(value > 255) value = 255;

    /* LedChannel: 0=Red, 1=Green, 2=Blue */
    Light light;
    if(channel == 0) {
        light = LightRed;
    } else if(channel == 1) {
        light = LightGreen;
    } else if(channel == 2) {
        light = LightBlue;
    } else {
        rpc_send_error(id, "invalid_color", "led_set");
        return;
    }

    furi_hal_light_set(light, (uint8_t)value);
    rpc_send_ok(id, "led_set");
    FURI_LOG_I("RPC", "led_set channel=%" PRIu32 " value=%" PRIu32, channel, value);
}
