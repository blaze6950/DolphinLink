/**
 * rpc_handlers_nfc.h — NFC RPC handler declarations
 *
 * Commands handled here:
 *   nfc_scan_start — streaming NFC protocol scanner
 */

#pragma once

#include <stdint.h>

void nfc_scan_start_handler(uint32_t id, const char* json);
