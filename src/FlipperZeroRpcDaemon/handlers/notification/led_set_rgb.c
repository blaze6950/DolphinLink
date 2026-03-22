/**
 * led_set_rgb.c — RPC handler implementation for the "led_set_rgb" command
 *
 * Sets all three RGB LED channels atomically in a single call using three
 * consecutive furi_hal_light_set() calls (red, green, blue).
 *
 * Wire format (request):
 *   {"c":25,"i":N,"r":0-255,"g":0-255,"b":0-255}
 *
 * Wire format (response — success):
 *   {"t":0,"i":N}
 *
 * Resources: none (0).
 * Thread: main (FuriEventLoop).
 */

#include "led_set_rgb.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"

#include <furi.h>
#include <furi_hal_light.h>
#include <inttypes.h>

void led_set_rgb_handler(uint32_t id, const char* json) {
    uint32_t r = 0, g = 0, b = 0;
    json_extract_uint32(json, "r", &r);
    json_extract_uint32(json, "g", &g);
    json_extract_uint32(json, "b", &b);
    if(r > 255) r = 255;
    if(g > 255) g = 255;
    if(b > 255) b = 255;

    furi_hal_light_set(LightRed, (uint8_t)r);
    furi_hal_light_set(LightGreen, (uint8_t)g);
    furi_hal_light_set(LightBlue, (uint8_t)b);
    rpc_send_ok(id, "led_set_rgb");
    FURI_LOG_I(
        "RPC",
        "led_set_rgb r=%" PRIu32 " g=%" PRIu32 " b=%" PRIu32,
        r,
        g,
        b);
}
