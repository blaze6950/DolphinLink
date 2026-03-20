# Command Registry

Cross-reference of every supported RPC command: JSON name, resource lock, stream flag,
C handler file, C# types file, and C# public extension method.

> **Source of truth**: `src/FlipperZeroRpcDaemon/core/rpc_dispatch.c` (C) and the
> `Commands/` + `Extensions/` trees (C#). Keep this table in sync when adding or
> removing commands.

## Table

| #  | Command                | Description                     | Resource  | Stream | C handler                                | C# types                                       | C# extension                                      |
|----|------------------------|---------------------------------|-----------|--------|------------------------------------------|------------------------------------------------|---------------------------------------------------|
| 1  | `ping`                 | Check connectivity              | —         | —      | `handlers/core/ping.c`                   | `Commands/Core/PingCommand.cs`                 | `FlipperPingExtensions.PingAsync`                 |
| 2  | `stream_close`         | Close an open stream            | —         | —      | `handlers/core/stream_close.c`           | `Commands/Core/StreamCloseCommand.cs`          | `FlipperStreamExtensions.StreamCloseAsync`        |
| 3  | `device_info`          | Read hardware/firmware info     | —         | —      | `handlers/system/device_info.c`          | `Commands/System/DeviceInfoCommand.cs`         | `FlipperSystemExtensions.DeviceInfoAsync`         |
| 4  | `daemon_info`          | Capability negotiation          | —         | —      | `handlers/system/daemon_info.c`          | `Commands/System/DaemonInfoCommand.cs`         | `FlipperSystemExtensions.DaemonInfoAsync`         |
| 5  | `daemon_stop`          | Stop the RPC daemon gracefully  | —         | —      | `handlers/system/daemon_stop.c`          | `Commands/System/DaemonStopCommand.cs`         | `FlipperSystemExtensions.DaemonStopAsync`         |
| 6  | `power_info`           | Read battery and charging state | —         | —      | `handlers/system/power_info.c`           | `Commands/System/PowerInfoCommand.cs`          | `FlipperSystemExtensions.PowerInfoAsync`          |
| 7  | `datetime_get`         | Read RTC date and time          | —         | —      | `handlers/system/datetime_get.c`         | `Commands/System/DatetimeGetCommand.cs`        | `FlipperSystemExtensions.DatetimeGetAsync`        |
| 8  | `datetime_set`         | Set RTC date and time           | —         | —      | `handlers/system/datetime_set.c`         | `Commands/System/DatetimeSetCommand.cs`        | `FlipperSystemExtensions.DatetimeSetAsync`        |
| 9  | `region_info`          | Read radio region/band config   | —         | —      | `handlers/system/region_info.c`          | `Commands/System/RegionInfoCommand.cs`         | `FlipperSystemExtensions.RegionInfoAsync`         |
| 10 | `frequency_is_allowed` | Check if frequency is permitted | —         | —      | `handlers/system/frequency_is_allowed.c` | `Commands/System/FrequencyIsAllowedCommand.cs` | `FlipperSystemExtensions.FrequencyIsAllowedAsync` |
| 11 | `reboot`               | Immediately reboot the Flipper  | —         | —      | `handlers/system/reboot.c`               | `Commands/System/RebootCommand.cs`             | `FlipperSystemExtensions.RebootAsync`             |
| 12 | `gpio_read`            | Read digital GPIO pin state     | —         | —      | `handlers/gpio/gpio_read.c`              | `Commands/Gpio/GpioReadCommand.cs`             | `FlipperGpioExtensions.GpioReadAsync`             |
| 13 | `gpio_write`           | Write digital GPIO pin state    | —         | —      | `handlers/gpio/gpio_write.c`             | `Commands/Gpio/GpioWriteCommand.cs`            | `FlipperGpioExtensions.GpioWriteAsync`            |
| 14 | `adc_read`             | Read analog pin voltage         | —         | —      | `handlers/gpio/adc_read.c`               | `Commands/Gpio/AdcReadCommand.cs`              | `FlipperGpioExtensions.AdcReadAsync`              |
| 15 | `gpio_set_5v`          | Enable or disable 5 V supply    | —         | —      | `handlers/gpio/gpio_set_5v.c`            | `Commands/Gpio/GpioSet5vCommand.cs`            | `FlipperGpioExtensions.GpioSet5vAsync`            |
| 16 | `gpio_watch_start`     | Stream GPIO pin change events   | —         | yes    | `handlers/gpio/gpio_watch_start.c`       | `Commands/Gpio/GpioWatchStartCommand.cs`       | `FlipperGpioExtensions.GpioWatchStartAsync`       |
| 17 | `ir_tx`                | Transmit IR signal by protocol  | `IR`      | —      | `handlers/ir/ir_tx.c`                    | `Commands/Ir/IrTxCommand.cs`                   | `FlipperIrExtensions.IrTxAsync`                   |
| 18 | `ir_tx_raw`            | Transmit raw IR pulse timings   | `IR`      | —      | `handlers/ir/ir_tx_raw.c`                | `Commands/Ir/IrTxRawCommand.cs`                | `FlipperIrExtensions.IrTxRawAsync`                |
| 19 | `ir_receive_start`     | Stream received IR signals      | `IR`      | yes    | `handlers/ir/ir_receive_start.c`         | `Commands/Ir/IrReceiveStartCommand.cs`         | `FlipperIrExtensions.IrReceiveStartAsync`         |
| 20 | `subghz_tx`            | Transmit Sub-GHz raw signal     | `SUBGHZ`  | —      | `handlers/subghz/subghz_tx.c`            | `Commands/SubGhz/SubGhzTxCommand.cs`           | `FlipperSubGhzExtensions.SubGhzTxAsync`           |
| 21 | `subghz_get_rssi`      | Read Sub-GHz RSSI at frequency  | `SUBGHZ`  | —      | `handlers/subghz/subghz_get_rssi.c`      | `Commands/SubGhz/SubGhzGetRssiCommand.cs`      | `FlipperSubGhzExtensions.SubGhzGetRssiAsync`      |
| 22 | `subghz_rx_start`      | Stream received Sub-GHz packets | `SUBGHZ`  | yes    | `handlers/subghz/subghz_rx_start.c`      | `Commands/SubGhz/SubGhzRxStartCommand.cs`      | `FlipperSubGhzExtensions.SubGhzRxStartAsync`      |
| 23 | `nfc_scan_start`       | Stream detected NFC tags        | `NFC`     | yes    | `handlers/nfc/nfc_scan_start.c`          | `Commands/Nfc/NfcScanStartCommand.cs`          | `FlipperNfcExtensions.NfcScanStartAsync`          |
| 24 | `led_set`              | Set single LED color            | —         | —      | `handlers/notification/led_set.c`        | `Commands/Notification/LedSetCommand.cs`       | `FlipperNotificationExtensions.LedSetAsync`       |
| 25 | `led_set_rgb`          | Set LED to RGB color            | —         | —      | `handlers/notification/led_set_rgb.c`    | `Commands/Notification/LedSetRgbCommand.cs`    | `FlipperNotificationExtensions.LedSetRgbAsync`    |
| 26 | `vibro`                | Trigger vibration motor         | —         | —      | `handlers/notification/vibro.c`          | `Commands/Notification/VibroCommand.cs`        | `FlipperNotificationExtensions.VibroAsync`        |
| 27 | `speaker_start`        | Start speaker tone at frequency | `SPEAKER` | —      | `handlers/notification/speaker_start.c`  | `Commands/Notification/SpeakerStartCommand.cs` | `FlipperNotificationExtensions.SpeakerStartAsync` |
| 28 | `speaker_stop`         | Stop speaker tone               | —         | —      | `handlers/notification/speaker_stop.c`   | `Commands/Notification/SpeakerStopCommand.cs`  | `FlipperNotificationExtensions.SpeakerStopAsync`  |
| 29 | `backlight`            | Set display backlight level     | —         | —      | `handlers/notification/backlight.c`      | `Commands/Notification/BacklightCommand.cs`    | `FlipperNotificationExtensions.BacklightAsync`    |
| 30 | `storage_info`         | Read filesystem stats           | —         | —      | `handlers/storage/storage_info.c`        | `Commands/Storage/StorageInfoCommand.cs`       | `FlipperStorageExtensions.StorageInfoAsync`       |
| 31 | `storage_list`         | List directory contents         | —         | —      | `handlers/storage/storage_list.c`        | `Commands/Storage/StorageListCommand.cs`       | `FlipperStorageExtensions.StorageListAsync`       |
| 32 | `storage_read`         | Read file contents              | —         | —      | `handlers/storage/storage_read.c`        | `Commands/Storage/StorageReadCommand.cs`       | `FlipperStorageExtensions.StorageReadAsync`       |
| 33 | `storage_write`        | Write file contents             | —         | —      | `handlers/storage/storage_write.c`       | `Commands/Storage/StorageWriteCommand.cs`      | `FlipperStorageExtensions.StorageWriteAsync`      |
| 34 | `storage_mkdir`        | Create directory                | —         | —      | `handlers/storage/storage_mkdir.c`       | `Commands/Storage/StorageMkdirCommand.cs`      | `FlipperStorageExtensions.StorageMkdirAsync`      |
| 35 | `storage_remove`       | Delete file or directory        | —         | —      | `handlers/storage/storage_remove.c`      | `Commands/Storage/StorageRemoveCommand.cs`     | `FlipperStorageExtensions.StorageRemoveAsync`     |
| 36 | `storage_stat`         | Stat file or directory          | —         | —      | `handlers/storage/storage_stat.c`        | `Commands/Storage/StorageStatCommand.cs`       | `FlipperStorageExtensions.StorageStatAsync`       |
| 37 | `lfrfid_read_start`    | Stream LF RFID card reads       | `RFID`    | yes    | `handlers/rfid/lfrfid_read_start.c`      | `Commands/Rfid/LfRfidReadStartCommand.cs`      | `FlipperRfidExtensions.LfRfidReadStartAsync`      |
| 38 | `ibutton_read_start`   | Stream iButton key reads        | `IBUTTON` | yes    | `handlers/ibutton/ibutton_read_start.c`  | `Commands/IButton/IButtonReadStartCommand.cs`  | `FlipperIButtonExtensions.IButtonReadStartAsync`  |
| 39 | `input_listen_start`   | Stream hardware button presses  | —         | yes    | `handlers/input/input_listen_start.c`    | `Commands/Input/InputListenStartCommand.cs`    | `FlipperInputExtensions.InputListenStartAsync`    |
| 40 | `ui_screen_acquire`    | Claim exclusive screen control  | `GUI`     | —      | `handlers/ui/ui_screen_acquire.c`        | `Commands/Ui/UiScreenAcquireCommand.cs`        | `FlipperUiExtensions.UiScreenAcquireAsync`        |
| 41 | `ui_screen_release`    | Release screen control          | —         | —      | `handlers/ui/ui_screen_release.c`        | `Commands/Ui/UiScreenReleaseCommand.cs`        | `FlipperUiExtensions.UiScreenReleaseAsync`        |
| 42 | `ui_draw_str`          | Queue draw-string operation     | —         | —      | `handlers/ui/ui_draw_str.c`              | `Commands/Ui/UiDrawStrCommand.cs`              | `FlipperUiExtensions.UiDrawStrAsync`              |
| 43 | `ui_draw_rect`         | Queue draw-rectangle operation  | —         | —      | `handlers/ui/ui_draw_rect.c`             | `Commands/Ui/UiDrawRectCommand.cs`             | `FlipperUiExtensions.UiDrawRectAsync`             |
| 44 | `ui_draw_line`         | Queue draw-line operation       | —         | —      | `handlers/ui/ui_draw_line.c`             | `Commands/Ui/UiDrawLineCommand.cs`             | `FlipperUiExtensions.UiDrawLineAsync`             |
| 45 | `ui_flush`             | Flush canvas to screen          | —         | —      | `handlers/ui/ui_flush.c`                 | `Commands/Ui/UiFlushCommand.cs`                | `FlipperUiExtensions.UiFlushAsync`                |
