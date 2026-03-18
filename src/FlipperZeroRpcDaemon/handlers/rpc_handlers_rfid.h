/**
 * rpc_handlers_rfid.h — LF RFID RPC handler declarations
 *
 * Commands handled here:
 *   lfrfid_read_start — streaming LF RFID tag reader
 */

#pragma once

#include <stdint.h>

void lfrfid_read_start_handler(uint32_t id, const char* json);
