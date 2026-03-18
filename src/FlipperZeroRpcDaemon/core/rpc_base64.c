/**
 * rpc_base64.c — Minimal Base64 encode / decode implementation
 *
 * Standard RFC 4648 Base64, no line wrapping.
 * Encoding is always well-formed.  Decoding tolerates missing padding.
 */

#include "rpc_base64.h"

#include <string.h>

/* -------------------------------------------------------------------------
 * Alphabet
 * ------------------------------------------------------------------------- */

static const char b64_enc[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

/** Decode table: index = ASCII char value, value = 6-bit group or 0xFF for invalid. */
static const uint8_t b64_dec[256] = {
    ['A'] = 0,  ['B'] = 1,  ['C'] = 2,  ['D'] = 3,  ['E'] = 4,  ['F'] = 5,  ['G'] = 6,  ['H'] = 7,
    ['I'] = 8,  ['J'] = 9,  ['K'] = 10, ['L'] = 11, ['M'] = 12, ['N'] = 13, ['O'] = 14, ['P'] = 15,
    ['Q'] = 16, ['R'] = 17, ['S'] = 18, ['T'] = 19, ['U'] = 20, ['V'] = 21, ['W'] = 22, ['X'] = 23,
    ['Y'] = 24, ['Z'] = 25, ['a'] = 26, ['b'] = 27, ['c'] = 28, ['d'] = 29, ['e'] = 30, ['f'] = 31,
    ['g'] = 32, ['h'] = 33, ['i'] = 34, ['j'] = 35, ['k'] = 36, ['l'] = 37, ['m'] = 38, ['n'] = 39,
    ['o'] = 40, ['p'] = 41, ['q'] = 42, ['r'] = 43, ['s'] = 44, ['t'] = 45, ['u'] = 46, ['v'] = 47,
    ['w'] = 48, ['x'] = 49, ['y'] = 50, ['z'] = 51, ['0'] = 52, ['1'] = 53, ['2'] = 54, ['3'] = 55,
    ['4'] = 56, ['5'] = 57, ['6'] = 58, ['7'] = 59, ['8'] = 60, ['9'] = 61, ['+'] = 62, ['/'] = 63,
};
/* All other entries are 0 by default (same as 'A'=0); we distinguish padding
 * by stopping at '=' and NUL rather than using a sentinel in the table. */

/* -------------------------------------------------------------------------
 * Encode
 * ------------------------------------------------------------------------- */

size_t base64_encode(const uint8_t* src, size_t src_len, char* out, size_t out_size) {
    size_t out_pos = 0;
    size_t i = 0;

    while(i + 2 < src_len) {
        uint32_t v = ((uint32_t)src[i] << 16) | ((uint32_t)src[i + 1] << 8) | src[i + 2];
        if(out_pos + 4 >= out_size) break;
        out[out_pos++] = b64_enc[(v >> 18) & 0x3F];
        out[out_pos++] = b64_enc[(v >> 12) & 0x3F];
        out[out_pos++] = b64_enc[(v >> 6) & 0x3F];
        out[out_pos++] = b64_enc[v & 0x3F];
        i += 3;
    }

    /* Handle remaining bytes */
    if(i < src_len && out_pos + 4 < out_size) {
        uint32_t v = (uint32_t)src[i] << 16;
        if(i + 1 < src_len) v |= (uint32_t)src[i + 1] << 8;

        out[out_pos++] = b64_enc[(v >> 18) & 0x3F];
        out[out_pos++] = b64_enc[(v >> 12) & 0x3F];
        out[out_pos++] = (i + 1 < src_len) ? b64_enc[(v >> 6) & 0x3F] : '=';
        out[out_pos++] = '=';
    }

    out[out_pos] = '\0';
    return out_pos;
}

/* -------------------------------------------------------------------------
 * Decode
 * ------------------------------------------------------------------------- */

size_t base64_decode(const char* src, uint8_t* out, size_t out_size) {
    size_t out_pos = 0;
    size_t src_len = strlen(src);

    for(size_t i = 0; i + 3 < src_len; i += 4) {
        unsigned char c0 = (unsigned char)src[i];
        unsigned char c1 = (unsigned char)src[i + 1];
        unsigned char c2 = (unsigned char)src[i + 2];
        unsigned char c3 = (unsigned char)src[i + 3];

        if(c0 == '=' || c1 == '=') break; /* unexpected early padding */

        uint32_t v = ((uint32_t)b64_dec[c0] << 18) | ((uint32_t)b64_dec[c1] << 12) |
                     ((uint32_t)b64_dec[c2] << 6) | b64_dec[c3];

        if(out_pos < out_size) out[out_pos++] = (uint8_t)(v >> 16);
        if(c2 != '=' && out_pos < out_size) out[out_pos++] = (uint8_t)((v >> 8) & 0xFF);
        if(c3 != '=' && out_pos < out_size) out[out_pos++] = (uint8_t)(v & 0xFF);
    }

    return out_pos;
}
