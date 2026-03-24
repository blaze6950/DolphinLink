/**
 * gpio_set_5v.c — gpio_set_5v RPC handler implementation
 *
 * Enables or disables the 5 V OTG supply rail on the external connector.
 *
 * Wire format (request):
 *   {"c":N,"i":M,"en":1}
 *
 * Wire format (response):
 *   {"t":0,"i":N}
 *
 * Error codes:
 *   missing_enable — "enable" field absent
 *
 * Warning: enabling OTG while an external 5 V source is present can cause
 * hardware damage.  No safety check is performed here.
 */

#include "gpio_set_5v.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"

#include <furi_hal_power.h>

void gpio_set_5v_handler(uint32_t id, const char* json, size_t offset) {
    bool enable = false;
    JsonValue val;
    if(!json_find(json, "en", offset, &val)) {
        rpc_send_error(id, "missing_enable", "gpio_set_5v");
        return;
    }
    json_value_bool(&val, &enable);

    if(enable) {
        furi_hal_power_enable_otg();
    } else {
        furi_hal_power_disable_otg();
    }

    rpc_send_ok(id, "gpio_set_5v");
}
