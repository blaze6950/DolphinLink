/**
 * rpc_base64.h — Minimal Base64 encode / decode helpers
 *
 * base64_encode(src, src_len, out, out_size)
 *   Encodes src_len bytes from src into a NUL-terminated Base64 ASCII string
 *   stored in out.  out_size must be at least BASE64_ENCODED_SIZE(src_len).
 *   Returns the number of Base64 characters written (excluding NUL).
 *
 * base64_decode(src, out, out_size)
 *   Decodes the NUL-terminated Base64 string src into out.
 *   Returns the number of decoded bytes, or 0 on error.
 *
 * BASE64_ENCODED_SIZE(n)  — worst-case output size (including NUL) for n bytes.
 * BASE64_DECODED_SIZE(n)  — worst-case decoded bytes for n Base64 characters.
 */

#pragma once

#include <stddef.h>
#include <stdint.h>

#define BASE64_ENCODED_SIZE(n) (((n) + 2) / 3 * 4 + 1)
#define BASE64_DECODED_SIZE(n) (((n) / 4) * 3 + 3)

size_t base64_encode(const uint8_t* src, size_t src_len, char* out, size_t out_size);
size_t base64_decode(const char* src, uint8_t* out, size_t out_size);
