/**
 * rpc_handlers_ir.h — Infrared RPC handler declarations
 *
 * Commands handled here:
 *   ir_tx            — transmit a decoded IR signal (protocol + address + command)
 *   ir_tx_raw        — transmit raw IR timing pairs
 *   ir_receive_start — open a stream of decoded IR receive events (migrated from rpc_handlers.c)
 */

#pragma once

#include <stdint.h>

void ir_tx_handler(uint32_t id, const char* json);
void ir_tx_raw_handler(uint32_t id, const char* json);
void ir_receive_start_handler(uint32_t id, const char* json);
