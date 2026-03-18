/**
 * rpc_handlers_system.h — System / device-info RPC handler declarations
 *
 * Commands handled here:
 *   device_info          — firmware version, model name, UID
 *   power_info           — battery percentage, voltage, charging state
 *   datetime_get         — current RTC date/time
 *   datetime_set         — write RTC date/time
 *   region_info          — region name + allowed band list
 *   frequency_is_allowed — check whether a given frequency is permitted
 */

#pragma once

#include <stdint.h>

void device_info_handler(uint32_t id, const char* json);
void power_info_handler(uint32_t id, const char* json);
void datetime_get_handler(uint32_t id, const char* json);
void datetime_set_handler(uint32_t id, const char* json);
void region_info_handler(uint32_t id, const char* json);
void frequency_is_allowed_handler(uint32_t id, const char* json);
