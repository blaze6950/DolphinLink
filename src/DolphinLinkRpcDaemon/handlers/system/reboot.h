#pragma once
#include <stdint.h>
#include <stddef.h>

void reboot_handler(uint32_t id, const char* json, size_t offset);
