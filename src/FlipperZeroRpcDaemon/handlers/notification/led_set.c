/**
 * led_set.c — RPC handler implementation for the "led_set" command
 *
 * Sets a single RGB LED channel (red, green, or blue) to an intensity value
 * in the range 0–255 using furi_hal_light_set().
 *
 * Wire format (request):
 *   {"id":N,"cmd":"led_set","color":"red"|"green"|"blue","value":0-255}
 *
 * Wire format (response — success):
 *   {"id":N,"status":"ok"}
 *
 * Wire format (response — error):
 *   {"id":N,"error":"missing_color"}  — "color" field absent
 *   {"id":N,"error":"invalid_color"}  — color string not recognised
 *
 * Resources: none (0).
 * Thread: main (FuriEventLoop).
 */

#include "led_set.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"

#include <furi.h>
#include <furi_hal_light.h>
#include <string.h>
#include <inttypes.h>

void led_set_handler(uint32_t id, const char* json) {
    char color[16] = {0};
    if(!json_extract_string(json, "color", color, sizeof(color))) {
        rpc_send_error(id, "missing_color", "led_set");
        return;
    }

    uint32_t value = 0;
    json_extract_uint32(json, "value", &value);
    if(value > 255) value = 255;

    Light light;
    if(strcmp(color, "red") == 0) {
        light = LightRed;
    } else if(strcmp(color, "green") == 0) {
        light = LightGreen;
    } else if(strcmp(color, "blue") == 0) {
        light = LightBlue;
    } else {
        rpc_send_error(id, "invalid_color", "led_set");
        return;
    }

    furi_hal_light_set(light, (uint8_t)value);
    rpc_send_ok(id, "led_set");
    FURI_LOG_I("RPC", "led_set color=%s value=%" PRIu32, color, value);
}
