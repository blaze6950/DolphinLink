using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Gpio;

/// <summary>
/// Enables or disables the 5 V supply rail on the Flipper's external header.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"gpio_set_5v","enable":true}</code>
///
/// Wire format (response):
/// <code>{"id":N,"status":"ok"}</code>
///
/// The 5 V rail powers external peripherals connected to the expansion port.
/// Disable it when not in use to reduce power consumption.
/// </summary>
public readonly struct GpioSet5vCommand : IRpcCommand<GpioSet5vResponse>
{
    /// <param name="enable"><c>true</c> to enable the 5 V rail; <c>false</c> to disable it.</param>
    public GpioSet5vCommand(bool enable) => Enable = enable;

    /// <summary><c>true</c> to enable the 5 V rail; <c>false</c> to disable it.</summary>
    public bool Enable { get; }

    /// <inheritdoc />
    public string CommandName => "gpio_set_5v";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteBoolean("enable", Enable);
    }
}

/// <summary>Response to <see cref="GpioSet5vCommand"/>.</summary>
public readonly struct GpioSet5vResponse : IRpcCommandResponse { }
