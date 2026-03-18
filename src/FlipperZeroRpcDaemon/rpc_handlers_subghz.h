/**
 * rpc_handlers_subghz.h — Sub-GHz RPC handler declarations
 *
 * Commands handled here:
 *   subghz_tx        — one-shot raw Sub-GHz TX (timing array)
 *   subghz_get_rssi  — read current RSSI at a given frequency
 *   subghz_rx_start  — open a streaming Sub-GHz RX session (migrated from rpc_handlers.c)
 */

#pragma once

#include <stdint.h>

void subghz_tx_handler(uint32_t id, const char* json);
void subghz_get_rssi_handler(uint32_t id, const char* json);
void subghz_rx_start_handler(uint32_t id, const char* json);
