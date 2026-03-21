/**
 * speaker_start.h — RPC handler declaration for the "speaker_start" command
 *
 * Starts a continuous tone on the Flipper Zero piezo speaker.  Acquires
 * RESOURCE_SPEAKER exclusively — subsequent calls return "resource_busy" until
 * "speaker_stop" is issued.
 *
 * Wire format (request):
 *   {"id":N,"cmd":"speaker_start","freq":440,"volume":128}
 *     freq   — frequency in Hz (uint32; passed as float to the HAL)
 *     volume — 0–255 mapped linearly to 0.0–1.0 HAL volume
 *
 * Wire format (response — success):
 *   {"t":0,"i":N}
 *
 * Wire format (response — error):
 *   {"t":0,"i":N,"e":"resource_busy"}  — HAL acquire failed
 *
 * Resources: RESOURCE_SPEAKER.
 */

#pragma once

#include <stdint.h>

/**
 * Handle the "speaker_start" RPC command.
 *
 * @param id   Request ID from the JSON message.
 * @param json Full JSON line (null-terminated) received from the host.
 */
void speaker_start_handler(uint32_t id, const char* json);
