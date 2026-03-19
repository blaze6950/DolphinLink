/**
 * gpio_pins.c — Shared GPIO pin table implementation
 *
 * Maps the 8 externally-accessible GPIO pins (labelled "1"–"8" on the
 * Flipper Zero connector) to their SDK GpioPin descriptors and ADC channels.
 *
 * This table is shared by gpio_read, gpio_write, adc_read, gpio_set_5v,
 * and gpio_watch_start — previously each file had a private copy; now
 * there is one canonical definition here.
 */

#include "gpio_pins.h"

#include <furi_hal_gpio.h>
#include <furi_hal_adc.h>
#include <furi_hal_resources.h>
#include <string.h>

/* -------------------------------------------------------------------------
 * Pin table
 * ------------------------------------------------------------------------- */

const GpioPinEntry gpio_pin_table[] = {
    /* label, pin,          adc_channel */
    {"1", &gpio_ext_pc0, FuriHalAdcChannel10},
    {"2", &gpio_ext_pc1, FuriHalAdcChannel11},
    {"3", &gpio_ext_pc3, FuriHalAdcChannel4},
    {"4", &gpio_ext_pb2, FuriHalAdcChannelNone},
    {"5", &gpio_ext_pb3, FuriHalAdcChannelNone},
    {"6", &gpio_ext_pa4, FuriHalAdcChannel9},
    {"7", &gpio_ext_pa6, FuriHalAdcChannel3},
    {"8", &gpio_ext_pa7, FuriHalAdcChannelNone},
    {NULL, NULL, FuriHalAdcChannelNone},
};

const GpioPinEntry* gpio_pin_entry_from_label(const char* label) {
    for(size_t i = 0; gpio_pin_table[i].label != NULL; i++) {
        if(strcmp(gpio_pin_table[i].label, label) == 0) {
            return &gpio_pin_table[i];
        }
    }
    return NULL;
}
