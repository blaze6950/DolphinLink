/**
 * rpc_json.h — Minimal JSON value extraction helpers
 *
 * All helpers operate on a NUL-terminated string and are pure functions —
 * they have no side effects and access no global state.
 *
 * json_extract_string(json, key, out, out_size)
 *   Finds  "key":"value"  and copies the decoded value into out.
 *   Returns true on success (including empty-string values).
 *
 * json_extract_uint32(json, key, out)
 *   Finds  "key":NNN  and stores the unsigned integer in *out.
 *   Returns true on success.
 *
 * json_extract_bool(json, key, out)
 *   Finds  "key":true  or  "key":false  and stores the value in *out.
 *   Returns true on success.
 *
 * json_extract_uint32_array(json, key, out, out_count, max_count)
 *   Finds  "key":[N,N,N,...]  and populates out[] with up to max_count values.
 *   Stores the actual number of elements in *out_count.
 *   Returns true if at least one element was read.
 *
 * Cursor variants (_at suffix)
 * ----------------------------
 * Each _at variant accepts an additional  const char** cursor  parameter.
 * On entry *cursor is the hint position to start searching from (may equal json).
 * On success *cursor is advanced past the extracted value so the next call
 * can continue from there without rescanning already-seen bytes.
 * If the key is not found starting from *cursor the function falls back to a
 * full scan from json before giving up.
 * Callers that don't need the cursor optimisation should use the plain variants.
 */

#pragma once

#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>

/* ---- Plain (full-scan) variants ---- */

bool json_extract_string(const char* json, const char* key, char* out, size_t out_size);
bool json_extract_uint32(const char* json, const char* key, uint32_t* out);
bool json_extract_bool(const char* json, const char* key, bool* out);
bool json_extract_uint32_array(
    const char* json,
    const char* key,
    uint32_t* out,
    size_t* out_count,
    size_t max_count);

/* ---- Cursor variants (search from *cursor, advance on success) ---- */

bool json_extract_string_at(
    const char* json,
    const char** cursor,
    const char* key,
    char* out,
    size_t out_size);
bool json_extract_uint32_at(
    const char* json,
    const char** cursor,
    const char* key,
    uint32_t* out);
bool json_extract_bool_at(
    const char* json,
    const char** cursor,
    const char* key,
    bool* out);
bool json_extract_uint32_array_at(
    const char* json,
    const char** cursor,
    const char* key,
    uint32_t* out,
    size_t* out_count,
    size_t max_count);
