/**
 * rpc_json.c -- Minimal zero-copy JSON value extraction
 *
 * Implements the api declared in rpc_json.h.  All parsing is done with a
 * single forward character walk from the caller-supplied offset hint; there
 * are no temporary buffers, no snprintf calls, no strstr calls, and no
 * fallback re-scans.  The input is assumed to be compact JSON (no whitespace)
 * as produced by the C# Utf8JsonWriter.
 */

#include "rpc_json.h"

/* -------------------------------------------------------------------------
 * Internal helpers
 * ------------------------------------------------------------------------- */

/**
 * Skip a single JSON value starting at pos.
 *
 * Handles strings (with \" escapes), numbers, booleans (true/false),
 * null, arrays (with nested values), and objects (with nested pairs).
 * Returns a pointer to the first character after the value, or pos if
 * the value is unrecognised (should not happen with well-formed JSON).
 */
static const char* json_skip_value(const char* pos) {
    if(!pos || *pos == '\0') return pos;

    if(*pos == '"') {
        /* String: scan to the closing un-escaped quote */
        pos++; /* skip opening '"' */
        while(*pos && *pos != '"') {
            if(*pos == '\\') pos++; /* skip escape character */
            if(*pos) pos++;
        }
        if(*pos == '"') pos++; /* skip closing '"' */
        return pos;
    }

    if(*pos == '[' || *pos == '{') {
        /* Array or object: track nesting depth */
        char open = *pos;
        char close = (open == '[') ? ']' : '}';
        int depth = 1;
        pos++;
        while(*pos && depth > 0) {
            if(*pos == '"') {
                /* Skip string so brackets inside strings don't count */
                pos++;
                while(*pos && *pos != '"') {
                    if(*pos == '\\') pos++;
                    if(*pos) pos++;
                }
                if(*pos == '"') pos++;
            } else {
                if(*pos == open) depth++;
                else if(*pos == close) depth--;
                pos++;
            }
        }
        return pos;
    }

    /* Number, boolean (true/false), null: scan until delimiter */
    while(*pos && *pos != ',' && *pos != '}' && *pos != ']') {
        pos++;
    }
    return pos;
}

/* -------------------------------------------------------------------------
 * Public API -- json_find
 * ------------------------------------------------------------------------- */

bool json_find(const char* json, const char* key, size_t hint, JsonValue* val) {
    const char* pos = json + hint;

    /* Skip the opening '{' or ',' separating fields when entering */
    if(*pos == '{' || *pos == ',') pos++;

    while(*pos) {
        /* Expect the opening '"' of a key */
        if(*pos != '"') {
            /* We hit '}' or something unexpected -- key not found */
            return false;
        }
        pos++; /* skip opening '"' of key */

        /* Compare key characters inline -- no snprintf, no strstr */
        const char* k = key;
        while(*pos && *pos != '"' && *k && *pos == *k) {
            pos++;
            k++;
        }
        bool key_matched = (*pos == '"' && *k == '\0');

        /* Advance pos to end of key string */
        if(!key_matched) {
            while(*pos && *pos != '"') {
                if(*pos == '\\') pos++; /* handle escaped chars in key */
                if(*pos) pos++;
            }
        }
        if(*pos == '"') pos++; /* skip closing '"' of key */

        /* Skip ':' separator */
        if(*pos != ':') return false;
        pos++;

        if(key_matched) {
            /* Found the key -- populate the JsonValue */
            val->start = pos;
            /* Measure value length by skipping it */
            const char* after = json_skip_value(pos);
            val->len = (size_t)(after - pos);
            val->offset = (size_t)(after - json);
            return true;
        }

        /* Key did not match -- skip over the value and continue */
        pos = json_skip_value(pos);

        /* Skip the ',' between fields (if present) */
        if(*pos == ',') pos++;
    }

    return false;
}

/* -------------------------------------------------------------------------
 * Public API -- value interpreters
 * ------------------------------------------------------------------------- */

bool json_value_uint32(const JsonValue* val, uint32_t* out) {
    const char* p = val->start;
    const char* end = p + val->len;

    if(p >= end || *p < '0' || *p > '9') return false;

    uint32_t result = 0;
    while(p < end && *p >= '0' && *p <= '9') {
        result = result * 10 + (uint32_t)(*p - '0');
        p++;
    }
    *out = result;
    return true;
}

bool json_value_bool(const JsonValue* val, bool* out) {
    const char* p = val->start;
    size_t len = val->len;

    if(len == 4 && p[0] == 't' && p[1] == 'r' && p[2] == 'u' && p[3] == 'e') {
        *out = true;
        return true;
    }
    if(len == 5 && p[0] == 'f' && p[1] == 'a' && p[2] == 'l' && p[3] == 's' && p[4] == 'e') {
        *out = false;
        return true;
    }
    /* V1 wire format: numeric 1/0 */
    if(len == 1 && *p == '1') {
        *out = true;
        return true;
    }
    if(len == 1 && *p == '0') {
        *out = false;
        return true;
    }
    return false;
}

bool json_value_string(const JsonValue* val, char* out, size_t out_size) {
    if(out_size == 0) return false;

    /* val->start already points past the opening '"'; val->len is content length */
    const char* p = val->start;
    const char* end = p + val->len;
    size_t i = 0;

    while(p < end && i < out_size - 1) {
        if(*p == '\\' && p + 1 < end) {
            p++; /* skip backslash */
            switch(*p) {
            case '"':
                out[i++] = '"';
                break;
            case '\\':
                out[i++] = '\\';
                break;
            case 'n':
                out[i++] = '\n';
                break;
            case 'r':
                out[i++] = '\r';
                break;
            case 't':
                out[i++] = '\t';
                break;
            default:
                out[i++] = *p;
                break;
            }
        } else {
            out[i++] = *p;
        }
        p++;
    }
    out[i] = '\0';
    return (i > 0 || val->len == 0); /* empty string "" is valid */
}

bool json_value_uint32_array(
    const JsonValue* val,
    uint32_t* out,
    size_t* out_count,
    size_t max_count) {
    *out_count = 0;

    const char* p = val->start;
    if(*p != '[') return false;
    p++; /* skip '[' */

    const char* array_end = val->start + val->len; /* points at ']' */

    while(p < array_end && *p != ']' && *out_count < max_count) {
        if(*p == ',') {
            p++;
            continue;
        }
        if(*p < '0' || *p > '9') return false; /* unexpected token */

        uint32_t v = 0;
        while(p < array_end && *p >= '0' && *p <= '9') {
            v = v * 10 + (uint32_t)(*p - '0');
            p++;
        }
        out[(*out_count)++] = v;
    }

    return (*out_count > 0);
}
