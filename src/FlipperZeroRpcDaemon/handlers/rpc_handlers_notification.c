/**
 * rpc_handlers_notification.c — Notification / LED / vibro / speaker RPC handlers
 *
 * led_set        — set one of the RGB LED channels (Red/Green/Blue) to 0–255
 * vibro          — enable or disable the vibration motor
 * speaker_start  — start a continuous tone; acquires RESOURCE_SPEAKER
 * speaker_stop   — stop the speaker; releases RESOURCE_SPEAKER
 * backlight      — set the LCD backlight brightness 0–255
 *
 * All handlers run on the main thread (FuriEventLoop).
 *
 * JSON protocol:
 *   led_set:       {"id":N,"cmd":"led_set","color":"red"|"green"|"blue","value":0-255}
 *   vibro:         {"id":N,"cmd":"vibro","enable":true|false}
 *   speaker_start: {"id":N,"cmd":"speaker_start","freq":440,"volume":128}
 *                    freq   — frequency in Hz (uint32, passed as float to the HAL)
 *                    volume — 0–255 mapped to 0.0–1.0
 *   speaker_stop:  {"id":N,"cmd":"speaker_stop"}
 *   backlight:     {"id":N,"cmd":"backlight","value":0-255}
 */

#include "rpc_handlers_notification.h"
#include "../core/rpc_globals.h"
#include "../core/rpc_response.h"
#include "../core/rpc_resource.h"
#include "../core/rpc_json.h"
#include "../core/rpc_cmd_log.h"

#include <furi.h>
#include <furi_hal_light.h>
#include <furi_hal_vibro.h>
#include <furi_hal_speaker.h>
#include <stdio.h>
#include <string.h>
#include <inttypes.h>

/* =========================================================
 * led_set
 * ========================================================= */

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

/* =========================================================
 * vibro
 * ========================================================= */

void vibro_handler(uint32_t id, const char* json) {
    bool enable = false;
    if(!json_extract_bool(json, "enable", &enable)) {
        rpc_send_error(id, "missing_enable", "vibro");
        return;
    }

    furi_hal_vibro_on(enable);
    rpc_send_ok(id, "vibro");
    FURI_LOG_I("RPC", "vibro enable=%d", (int)enable);
}

/* =========================================================
 * speaker_start
 * ========================================================= */

void speaker_start_handler(uint32_t id, const char* json) {
    uint32_t freq = 440;
    uint32_t volume_raw = 128; /* 0–255 */

    json_extract_uint32(json, "freq", &freq);
    json_extract_uint32(json, "volume", &volume_raw);
    if(volume_raw > 255) volume_raw = 255;

    float volume = (float)volume_raw / 255.0f;

    /* resource_acquire already called by dispatcher */
    if(!furi_hal_speaker_acquire(1000)) {
        rpc_send_error(id, "resource_busy", "speaker_start");
        resource_release(RESOURCE_SPEAKER);
        return;
    }

    furi_hal_speaker_start((float)freq, volume);
    rpc_send_ok(id, "speaker_start");
    FURI_LOG_I("RPC", "speaker_start freq=%" PRIu32 " volume=%" PRIu32, freq, volume_raw);
}

/* =========================================================
 * speaker_stop
 * ========================================================= */

void speaker_stop_handler(uint32_t id, const char* json) {
    UNUSED(json);

    furi_hal_speaker_stop();
    furi_hal_speaker_release();
    resource_release(RESOURCE_SPEAKER);

    rpc_send_ok(id, "speaker_stop");
    FURI_LOG_I("RPC", "speaker_stop");
}

/* =========================================================
 * backlight
 * ========================================================= */

void backlight_handler(uint32_t id, const char* json) {
    uint32_t value = 255;
    json_extract_uint32(json, "value", &value);
    if(value > 255) value = 255;

    furi_hal_light_set(LightBacklight, (uint8_t)value);
    rpc_send_ok(id, "backlight");
    FURI_LOG_I("RPC", "backlight value=%" PRIu32, value);
}
