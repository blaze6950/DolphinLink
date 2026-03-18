/**
 * rpc_handlers.h — RPC command handler declarations
 *
 * Only ping and stream_close remain here.  All other handlers have been
 * migrated to their subsystem-specific header files:
 *   rpc_handlers_system.h       — device_info, power_info, datetime_*, region_info, frequency_is_allowed
 *   rpc_handlers_gpio.h         — gpio_read, gpio_write, adc_read, gpio_set_5v, gpio_watch_start
 *   rpc_handlers_ir.h           — ir_tx, ir_tx_raw, ir_receive_start
 *   rpc_handlers_subghz.h       — subghz_tx, subghz_get_rssi, subghz_rx_start
 *   rpc_handlers_nfc.h          — nfc_scan_start
 *   rpc_handlers_notification.h — led_set, vibro, speaker_start, speaker_stop, backlight
 *   rpc_handlers_storage.h      — storage_info, storage_list, storage_read, storage_write,
 *                                  storage_mkdir, storage_remove, storage_stat
 *   rpc_handlers_rfid.h         — lfrfid_read_start
 *   rpc_handlers_ibutton.h      — ibutton_read_start
 *
 * Handler signature: void handler(uint32_t request_id, const char* json)
 */

#pragma once

#include <stdint.h>

/* Simple request-response */
void ping_handler(uint32_t id, const char* json);

/* Stream lifecycle */
void stream_close_handler(uint32_t id, const char* json);
