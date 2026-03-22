/**
 * backlight.c — RPC handler implementation for the "backlight" command
 *
 * Sets the LCD backlight brightness to a value in the range 0–255 using
 * furi_hal_light_set(LightBacklight, value).
 *
 * Wire format (request):
 *   {"c":N,"i":M,"vl":0-255}
 *
 * Wire format (response — success):
 *   {"t":0,"i":N}
 *
 * Resources: none (0).
 * Thread: main (FuriEventLoop).
 */

#include "backlight.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"

#include <furi.h>
#include <furi_hal_light.h>
#include <inttypes.h>

void backlight_handler(uint32_t id, const char* json) {
    uint32_t value = 255;
    json_extract_uint32(json, "vl", &value);
    if(value > 255) value = 255;

    furi_hal_light_set(LightBacklight, (uint8_t)value);
    rpc_send_ok(id, "backlight");
    FURI_LOG_I("RPC", "backlight value=%" PRIu32, value);
}
