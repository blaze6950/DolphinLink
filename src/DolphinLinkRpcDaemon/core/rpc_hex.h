/**
 * rpc_hex.h — shared hex-encoding helper for RPC handlers
 */

#pragma once

#include <stddef.h>
#include <stdint.h>
#include <stdio.h>

/**
 * Encode `len` bytes of `data` as uppercase hex into `out`.
 * `out` must be at least `len * 2 + 1` bytes.
 * Returns the number of hex characters written (excluding the NUL terminator).
 */
static inline size_t hex_format(char* out, size_t out_size, const uint8_t* data, size_t len) {
    size_t written = 0;
    for(size_t i = 0; i < len && written + 2 < out_size; i++) {
        snprintf(out + written, out_size - written, "%02X", data[i]);
        written += 2;
    }
    return written;
}
