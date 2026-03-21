using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Ir;

/// <summary>
/// Transmits a decoded IR signal (protocol + address + command) via the Flipper's IR LED.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"ir_tx","protocol":"NEC","address":32,"command":11}</code>
///
/// Wire format (response):
/// <code>{"t":0,"i":N}</code>
///
/// Requires the IR hardware resource; returns <c>resource_busy</c> if another
/// IR command is active (e.g. <c>ir_receive_start</c>).
/// </summary>
public readonly struct IrTxCommand : IRpcCommand<IrTxResponse>
{
    /// <param name="protocol">IR protocol to use for transmission.</param>
    /// <param name="address">Device address field of the IR frame.</param>
    /// <param name="command">Command field of the IR frame.</param>
    public IrTxCommand(IrProtocol protocol, uint address, uint command)
    {
        Protocol = protocol;
        Address = address;
        Command = command;
    }

    /// <summary>IR protocol.</summary>
    public IrProtocol Protocol { get; }

    /// <summary>Device address field.</summary>
    public uint Address { get; }

    /// <summary>Command code field.</summary>
    public uint Command { get; }

    /// <inheritdoc />
    public string CommandName => "ir_tx";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteString("protocol", Protocol.ToString());
        writer.WriteNumber("address", Address);
        writer.WriteNumber("command", Command);
    }
}

/// <summary>Response to <see cref="IrTxCommand"/>.</summary>
public readonly struct IrTxResponse : IRpcCommandResponse { }
