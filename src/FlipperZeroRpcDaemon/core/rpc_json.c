/**
 * rpc_json.c — Minimal JSON value extraction helpers
 *
 * Both extraction functions share a common internal helper (json_find_value)
 * that locates the start of a value for a given key.
 */

#include "rpc_json.h"

#include <string.h>
#include <stdio.h>

/* -------------------------------------------------------------------------
 * Internal helpers
 * ------------------------------------------------------------------------- */

/**
 * Finds the value portion for a given key in a flat JSON object.
 *
 * Builds the search token  "key":  then returns a pointer to the first
 * non-whitespace character of the value, or NULL if the key is absent.
 */
static const char* json_find_value(const char* json, const char* key) {
    char token[72];
    snprintf(token, sizeof(token), "\"%s\":", key);

    const char* pos = strstr(json, token);
    if(!pos) return NULL;

    pos += strlen(token);

    /* Skip optional whitespace between ':' and value */
    while(*pos == ' ' || *pos == '\t')
        pos++;

    return pos;
}

/**
 * Like json_find_value but starts searching from *hint first.
 * Falls back to a full scan from json if not found at the hint position.
 * On success *hint is updated to point just past the value start.
 */
static const char* json_find_value_at(const char* json, const char** hint, const char* key) {
    char token[72];
    snprintf(token, sizeof(token), "\"%s\":", key);
    size_t token_len = strlen(token);

    /* Try from hint position first */
    const char* pos = strstr(*hint, token);

    /* Fall back to full scan if not found from hint */
    if(!pos && *hint != json) {
        pos = strstr(json, token);
    }

    if(!pos) return NULL;

    pos += token_len;

    /* Skip optional whitespace between ':' and value */
    while(*pos == ' ' || *pos == '\t')
        pos++;

    /* Advance hint past the value start so next call continues forward */
    *hint = pos;

    return pos;
}

/* -------------------------------------------------------------------------
 * Public API — plain (full-scan) variants
 * ------------------------------------------------------------------------- */

bool json_extract_string(const char* json, const char* key, char* out, size_t out_size) {
    const char* pos = json_find_value(json, key);
    if(!pos) return false;

    if(*pos != '"') return false;
    pos++; /* skip opening quote */

    size_t i = 0;
    while(*pos && *pos != '"' && i < out_size - 1) {
        /* Handle simple escape sequences */
        if(*pos == '\\' && *(pos + 1)) {
            pos++;
            switch(*pos) {
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
                out[i++] = *pos;
                break;
            }
        } else {
            out[i++] = *pos;
        }
        pos++;
    }
    out[i] = '\0';
    return (i > 0 || *pos == '"'); /* empty string "" is valid */
}

bool json_extract_uint32(const char* json, const char* key, uint32_t* out) {
    const char* pos = json_find_value(json, key);
    if(!pos) return false;

    /* Must start with a digit */
    if(*pos < '0' || *pos > '9') return false;

    uint32_t val = 0;
    while(*pos >= '0' && *pos <= '9') {
        val = val * 10 + (uint32_t)(*pos - '0');
        pos++;
    }
    *out = val;
    return true;
}

bool json_extract_bool(const char* json, const char* key, bool* out) {
    const char* pos = json_find_value(json, key);
    if(!pos) return false;

    if(strncmp(pos, "true", 4) == 0) {
        *out = true;
        return true;
    }
    if(strncmp(pos, "false", 5) == 0) {
        *out = false;
        return true;
    }
    /* V1 wire format: numeric 1/0 */
    if(*pos == '1') {
        *out = true;
        return true;
    }
    if(*pos == '0') {
        *out = false;
        return true;
    }
    return false;
}

bool json_extract_uint32_array(
    const char* json,
    const char* key,
    uint32_t* out,
    size_t* out_count,
    size_t max_count) {
    *out_count = 0;
    const char* pos = json_find_value(json, key);
    if(!pos) return false;
    if(*pos != '[') return false;
    pos++; /* skip '[' */

    while(*pos && *pos != ']' && *out_count < max_count) {
        /* Skip whitespace and commas */
        while(*pos == ' ' || *pos == '\t' || *pos == ',')
            pos++;
        if(*pos == ']' || *pos == '\0') break;

        if(*pos < '0' || *pos > '9') return false; /* unexpected token */

        uint32_t val = 0;
        while(*pos >= '0' && *pos <= '9') {
            val = val * 10 + (uint32_t)(*pos - '0');
            pos++;
        }
        out[(*out_count)++] = val;
    }
    return (*out_count > 0);
}

/* -------------------------------------------------------------------------
 * Public API — cursor (_at) variants
 * ------------------------------------------------------------------------- */

bool json_extract_string_at(
    const char* json,
    const char** cursor,
    const char* key,
    char* out,
    size_t out_size) {
    const char* pos = json_find_value_at(json, cursor, key);
    if(!pos) return false;

    if(*pos != '"') return false;
    pos++; /* skip opening quote */

    size_t i = 0;
    while(*pos && *pos != '"' && i < out_size - 1) {
        if(*pos == '\\' && *(pos + 1)) {
            pos++;
            switch(*pos) {
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
                out[i++] = *pos;
                break;
            }
        } else {
            out[i++] = *pos;
        }
        pos++;
    }
    out[i] = '\0';

    /* Advance cursor past the closing quote */
    if(*pos == '"') pos++;
    *cursor = pos;

    return (i > 0 || *(pos - 1) == '"');
}

bool json_extract_uint32_at(
    const char* json,
    const char** cursor,
    const char* key,
    uint32_t* out) {
    const char* pos = json_find_value_at(json, cursor, key);
    if(!pos) return false;

    if(*pos < '0' || *pos > '9') return false;

    uint32_t val = 0;
    while(*pos >= '0' && *pos <= '9') {
        val = val * 10 + (uint32_t)(*pos - '0');
        pos++;
    }
    *out = val;
    *cursor = pos;
    return true;
}

bool json_extract_bool_at(
    const char* json,
    const char** cursor,
    const char* key,
    bool* out) {
    const char* pos = json_find_value_at(json, cursor, key);
    if(!pos) return false;

    if(strncmp(pos, "true", 4) == 0) {
        *out = true;
        *cursor = pos + 4;
        return true;
    }
    if(strncmp(pos, "false", 5) == 0) {
        *out = false;
        *cursor = pos + 5;
        return true;
    }
    /* V1 wire format: numeric 1/0 */
    if(*pos == '1') {
        *out = true;
        *cursor = pos + 1;
        return true;
    }
    if(*pos == '0') {
        *out = false;
        *cursor = pos + 1;
        return true;
    }
    return false;
}

bool json_extract_uint32_array_at(
    const char* json,
    const char** cursor,
    const char* key,
    uint32_t* out,
    size_t* out_count,
    size_t max_count) {
    *out_count = 0;
    const char* pos = json_find_value_at(json, cursor, key);
    if(!pos) return false;
    if(*pos != '[') return false;
    pos++; /* skip '[' */

    while(*pos && *pos != ']' && *out_count < max_count) {
        while(*pos == ' ' || *pos == '\t' || *pos == ',')
            pos++;
        if(*pos == ']' || *pos == '\0') break;

        if(*pos < '0' || *pos > '9') return false;

        uint32_t val = 0;
        while(*pos >= '0' && *pos <= '9') {
            val = val * 10 + (uint32_t)(*pos - '0');
            pos++;
        }
        out[(*out_count)++] = val;
    }

    /* Advance cursor past the closing ']' if present */
    if(*pos == ']') pos++;
    *cursor = pos;

    return (*out_count > 0);
}
