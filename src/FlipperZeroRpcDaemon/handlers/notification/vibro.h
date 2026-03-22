/**
 * vibro.h — RPC handler declaration for the "vibro" command
 *
 * Enables or disables the Flipper Zero vibration motor.
 *
 * Wire format (request):
 *   {"c":26,"i":N,"en":1}
 *     en — 1 = enable vibration, 0 = disable
 *
 * Wire format (response — success):
 *   {"t":0,"i":N}
 *
 * Wire format (response — error):
 *   {"t":0,"i":N,"e":"missing_enable"}  — "en" field absent
 *
 * Resources: none (0).
 */

#pragma once

#include <stdint.h>
#include <stddef.h>

/**
 * Handle the "vibro" RPC command.
 *
 * @param id     Request ID from the JSON message.
 * @param json   Full JSON line (null-terminated) received from the host.
 * @param offset Byte offset past the already-parsed "c" and "i" fields.
 */
void vibro_handler(uint32_t id, const char* json, size_t offset);
