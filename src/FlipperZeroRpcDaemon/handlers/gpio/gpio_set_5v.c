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

void gpio_set_5v_handler(uint32_t id, const char* json) {
    bool enable = false;
    if(!json_extract_bool(json, "en", &enable)) {
        rpc_send_error(id, "missing_enable", "gpio_set_5v");
        return;
    }

    if(enable) {
        furi_hal_power_enable_otg();
    } else {
        furi_hal_power_disable_otg();
    }

    rpc_send_ok(id, "gpio_set_5v");
}
