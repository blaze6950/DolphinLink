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
 * Internal helper
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

/* -------------------------------------------------------------------------
 * Public API
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
