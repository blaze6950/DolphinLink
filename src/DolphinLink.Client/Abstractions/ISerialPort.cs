namespace DolphinLink.Client.Abstractions;

/// <summary>
/// Abstraction over a physical serial port, decoupling the NDJSON RPC transport
/// and the native protobuf bootstrap transport from any specific serial
/// implementation (<see cref="System.IO.Ports.SerialPort"/>, WebSerial, etc.).
///
/// <para>
/// Both the <c>SerialPortTransport</c> (NDJSON line framing) and the bootstrapper's
/// <c>NativeRpcTransport</c> (varint-prefixed protobuf framing) build their protocol
/// framing on top of the raw <see cref="Stream"/> exposed by this interface.
/// Providing a single common abstraction means a single WebSerial implementation
/// in the browser can power both transport stacks without duplication.
/// </para>
///
/// Lifetime contract:
/// <list type="number">
///   <item>Call <see cref="OpenAsync"/> exactly once before any I/O.</item>
///   <item>Access <see cref="Stream"/> only after <see cref="OpenAsync"/> completes.</item>
///   <item>Call <see cref="DisposeAsync"/> to release the port; all pending I/O
///     must be unblocked by the implementation before or during disposal.</item>
/// </list>
/// </summary>
public interface ISerialPort : IAsyncDisposable
{
    /// <summary>
    /// Opens the serial port and makes <see cref="Stream"/> available.
    /// Must be called exactly once, before any other member.
    /// </summary>
    ValueTask OpenAsync(CancellationToken ct = default);

    /// <summary>
    /// The raw byte stream for this port.  Valid only after <see cref="OpenAsync"/>
    /// has completed successfully.
    /// </summary>
    Stream Stream { get; }

    /// <summary>
    /// Asserts or de-asserts the DTR (Data Terminal Ready) modem control line.
    ///
    /// <para>
    /// The native RPC bootstrap sequence requires toggling DTR low→high to trigger
    /// the Flipper's CLI shell.  WebSerial exposes this via
    /// <c>port.setSignals({ dataTerminalReady })</c>, which is asynchronous —
    /// hence the async signature here even though
    /// <see cref="System.IO.Ports.SerialPort.DtrEnable"/> is a synchronous property.
    /// </para>
    /// </summary>
    ValueTask SetDtrAsync(bool enabled, CancellationToken ct = default);

    /// <summary>
    /// Gets or sets the read timeout in milliseconds.
    /// Use <c>-1</c> (or <see cref="System.IO.Ports.SerialPort.InfiniteTimeout"/>) for no timeout.
    ///
    /// <para>
    /// The bootstrapper handshake temporarily reduces the timeout to detect a missing
    /// CLI prompt within a bounded window, then restores infinite timeout for protobuf I/O.
    /// Implementations that cannot support per-read timeouts (e.g. WebSerial) should
    /// accept the value and rely on <see cref="CancellationToken"/>-based cancellation
    /// on the caller side instead.
    /// </para>
    /// </summary>
    int ReadTimeout { get; set; }

    /// <summary>
    /// Gets or sets the write timeout in milliseconds.
    /// Use <c>-1</c> for no timeout.
    /// </summary>
    int WriteTimeout { get; set; }
}
