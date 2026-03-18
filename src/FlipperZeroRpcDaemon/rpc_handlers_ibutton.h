/**
 * rpc_handlers_ibutton.h — iButton RPC handler declarations
 *
 * Commands handled here:
 *   ibutton_read_start — streaming iButton key reader
 */

#pragma once

#include <stdint.h>

void ibutton_read_start_handler(uint32_t id, const char* json);
