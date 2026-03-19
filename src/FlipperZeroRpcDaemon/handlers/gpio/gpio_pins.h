/**
 * gpio_pins.h — Shared GPIO pin table for all GPIO command handlers
 *
 * Defines the GpioPinEntry type and the gpio_pin_table[] that maps the
 * user-facing pin label strings ("1"–"8") to the Flipper GPIO descriptors
 * and their associated ADC channels.
 *
 * Used by: gpio_read, gpio_write, adc_read, gpio_set_5v, gpio_watch_start.
 *
 * Pin map (Flipper Zero external connector, left-to-right):
 *   Label  MCU pin   ADC channel
 *   "1"    PC0       ADC1_IN10
 *   "2"    PC1       ADC1_IN11
 *   "3"    PC3       ADC1_IN4
 *   "4"    PB2       (no ADC)
 *   "5"    PB3       (no ADC)
 *   "6"    PA4       ADC1_IN9
 *   "7"    PA6       ADC1_IN3
 *   "8"    PA7       (no ADC)
 */

#pragma once

#include <furi_hal_gpio.h>
#include <furi_hal_adc.h>

/** One entry in the static pin table. */
typedef struct {
    const char* label;             /**< User-facing pin name ("1"–"8"). */
    const GpioPin* pin;            /**< Pointer to the Flipper SDK GPIO descriptor. */
    FuriHalAdcChannel adc_channel; /**< FuriHalAdcChannelNone if the pin has no ADC. */
} GpioPinEntry;

/**
 * Null-terminated table of all externally-accessible GPIO pins.
 * The last entry has label == NULL.
 */
extern const GpioPinEntry gpio_pin_table[];

/**
 * Look up a pin entry by its label string.
 *
 * @param label  User-supplied pin label (e.g. "1", "7").
 * @return Pointer to the matching GpioPinEntry, or NULL if not found.
 */
const GpioPinEntry* gpio_pin_entry_from_label(const char* label);
