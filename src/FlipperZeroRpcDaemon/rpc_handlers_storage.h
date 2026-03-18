/**
 * rpc_handlers_storage.h — Storage RPC handler declarations
 *
 * Commands handled here:
 *   storage_info   — filesystem label, total/free bytes
 *   storage_list   — directory listing
 *   storage_read   — read a file and return its contents as Base64
 *   storage_write  — write Base64-encoded data to a file
 *   storage_mkdir  — create a directory
 *   storage_remove — remove a file or empty directory
 *   storage_stat   — stat a file or directory (size, is_dir)
 */

#pragma once

#include <stdint.h>

void storage_info_handler(uint32_t id, const char* json);
void storage_list_handler(uint32_t id, const char* json);
void storage_read_handler(uint32_t id, const char* json);
void storage_write_handler(uint32_t id, const char* json);
void storage_mkdir_handler(uint32_t id, const char* json);
void storage_remove_handler(uint32_t id, const char* json);
void storage_stat_handler(uint32_t id, const char* json);
