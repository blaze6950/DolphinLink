/**
 * rpc_json.h — Minimal zero-copy JSON value extraction
 *
 * Designed for compact (no-whitespace) NDJSON as produced by the C# client.
 * All parsing is a single forward scan from a caller-supplied offset hint;
 * there is no fallback re-scan.  If the expected key is not found scanning
 * forward from the hint the function returns false, making field-order bugs
 * immediately visible instead of silently degrading performance.
 *
 * ---
 *
 * JsonValue -- zero-copy view of a value inside the original JSON string.
 *
 *   start   Pointer to the first character of the value content:
 *             strings  -> points past the opening '"' (content only, no quotes)
 *             numbers  -> points at the first digit
 *             bools    -> points at 't' or 'f' (or '1'/'0' for V1 numeric wire)
 *             arrays   -> points at '['
 *             objects  -> points at '{'
 *   len     Length of the value content (strings: excludes quotes).
 *   offset  Byte offset in the original json string just past this value.
 *           Pass as the hint to the next json_find() call to continue forward.
 *
 * ---
 *
 * json_find(json, key, hint, val)
 *   Scan json[hint..] for "key":<value>.  On success fills *val and returns
 *   true.  Skips non-matching key-value pairs by walking over their values
 *   structurally (handles nested strings, arrays, objects).  Returns false if
 *   the key is not found before the end of the object.
 *
 * json_value_uint32(val, out)
 *   Parse a decimal integer from a JsonValue obtained by json_find().
 *
 * json_value_bool(val, out)
 *   Parse true/false (or numeric 1/0 for V1 wire compat) from a JsonValue.
 *
 * json_value_string(val, out, out_size)
 *   Copy + unescape a string JsonValue into a caller-supplied buffer.
 *   Handles \" \\ \n \r \t escape sequences.
 *
 * json_value_uint32_array(val, out, out_count, max_count)
 *   Parse a JSON array of unsigned decimal integers from a JsonValue.
 *   Populates out[] with up to max_count elements; sets *out_count to the
 *   actual count.  Returns true if at least one element was read.
 */

#pragma once

#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>

/** Zero-copy view of a JSON value within the original string. */
typedef struct {
    const char* start; /**< First char of value content (see header for details) */
    size_t len;        /**< Length of value content                               */
    size_t offset;     /**< Byte offset past this value -- use as next hint       */
} JsonValue;

/**
 * Scan json[hint..] for "key":<value> (compact JSON, no whitespace).
 * No fallback: returns false if the key is not found scanning forward from hint.
 */
bool json_find(const char* json, const char* key, size_t hint, JsonValue* val);

/** Interpret a JsonValue as a uint32. */
bool json_value_uint32(const JsonValue* val, uint32_t* out);

/** Interpret a JsonValue as a bool (true/false or 1/0). */
bool json_value_bool(const JsonValue* val, bool* out);

/** Copy + unescape a string JsonValue into out (NUL-terminated). */
bool json_value_string(const JsonValue* val, char* out, size_t out_size);

/**
 * Parse a JsonValue array of uint32 values.
 * Fills out[] with up to max_count elements; sets *out_count to the actual
 * element count.  Returns true if at least one element was read.
 */
bool json_value_uint32_array(
    const JsonValue* val,
    uint32_t* out,
    size_t* out_count,
    size_t max_count);
