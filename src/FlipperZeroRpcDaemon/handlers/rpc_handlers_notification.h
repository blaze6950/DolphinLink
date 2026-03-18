/**
 * rpc_handlers_notification.h — Notification / LED / vibro / speaker RPC handler declarations
 *
 * Commands handled here:
 *   led_set        — set an LED colour channel intensity (0–255)
 *   vibro          — enable or disable the vibration motor
 *   speaker_start  — start a continuous tone on the piezo speaker
 *   speaker_stop   — stop the piezo speaker
 *   backlight      — set backlight intensity (0–255)
 */

#pragma once

#include <stdint.h>

void led_set_handler(uint32_t id, const char* json);
void vibro_handler(uint32_t id, const char* json);
void speaker_start_handler(uint32_t id, const char* json);
void speaker_stop_handler(uint32_t id, const char* json);
void backlight_handler(uint32_t id, const char* json);
