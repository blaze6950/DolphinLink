using FlipperZero.NET.Commands;
using FlipperZero.NET.Transport;

namespace FlipperZero.NET;

/// <summary>
    /// Connection-behaviour options for <see cref="FlipperRpcClient"/>.
    ///
    /// Both <c>default</c> and <c>new FlipperRpcClientOptions()</c> produce an
    /// instance with the standard heartbeat timing (3 s interval, 10 s timeout)
    /// and the <see cref="RgbColor.DotNetPurple"/> LED indicator.
    /// Use a <c>with</c>-expression or object initialiser to override individual
    /// values:
    ///
    /// <code>
    /// // Zero-config — default 3 s heartbeat, 10 s timeout, purple LED:
    /// new FlipperRpcClient(transport)
    ///
    /// // Disable the LED indicator:
    /// new FlipperRpcClient(transport, new FlipperRpcClientOptions
    /// {
    ///     LedIndicatorColor = null,
    /// })
    ///
    /// // Accelerated timing, custom LED color:
    /// new FlipperRpcClient(transport, new FlipperRpcClientOptions
    /// {
    ///     HeartbeatInterval = TimeSpan.FromSeconds(1),
    ///     Timeout           = TimeSpan.FromSeconds(4),
    ///     LedIndicatorColor = RgbColor.Cyan,
    /// })
    ///
    /// // Enable daemon-side per-request timing diagnostics:
    /// new FlipperRpcClient(transport, new FlipperRpcClientOptions
    /// {
    ///     DaemonDiagnostics = true,
    /// })
    /// </code>
    /// </summary>
    public readonly record struct FlipperRpcClientOptions
{
    // Raw backing values — TimeSpan.Zero means "use the default".
    private readonly TimeSpan _heartbeatInterval;
    private readonly TimeSpan _timeout;
    // Sentinel: null means "not explicitly set — use the default (DotNetPurple)".
    // Callers opt out by setting the property to RgbColor.Off or any other value.
    // We need one extra level of wrapping to distinguish "not set" from "explicitly null".
    private readonly bool _ledIndicatorColorSet;
    private readonly RgbColor? _ledIndicatorColor;

    /// <summary>
    /// How long outbound silence is allowed before a keep-alive frame is sent.
    /// Defaults to <see cref="HeartbeatTransport.DefaultHeartbeatInterval"/> (3 s).
    /// </summary>
    public TimeSpan HeartbeatInterval
    {
        get => _heartbeatInterval == TimeSpan.Zero
            ? HeartbeatTransport.DefaultHeartbeatInterval
            : _heartbeatInterval;
        init => _heartbeatInterval = value;
    }

    /// <summary>
    /// How long inbound silence is allowed before the connection is declared lost.
    /// Must be strictly greater than <see cref="HeartbeatInterval"/>.
    /// Defaults to <see cref="HeartbeatTransport.DefaultTimeout"/> (10 s).
    /// </summary>
    public TimeSpan Timeout
    {
        get => _timeout == TimeSpan.Zero
            ? HeartbeatTransport.DefaultTimeout
            : _timeout;
        init => _timeout = value;
    }

    /// <summary>
    /// Optional LED connection indicator colour sent to the daemon during the
    /// <c>configure</c> handshake.
    ///
    /// When non-null, the daemon turns on the Flipper's RGB LED with this colour
    /// while the connection is active and turns it off when the connection is lost.
    /// The indicator is scoped to a single connection lifecycle — it is cleared on
    /// every disconnect so the next session starts with the LED off.
    ///
    /// Defaults to <see cref="RgbColor.DotNetPurple"/> (<c>#512BD4</c>) so every
    /// connection shows the .NET brand colour without any extra configuration.
    /// Set to <c>null</c> to disable the LED indicator entirely.
    /// </summary>
    public RgbColor? LedIndicatorColor
    {
        get => _ledIndicatorColorSet ? _ledIndicatorColor : RgbColor.DotNetPurple;
        init
        {
            _ledIndicatorColor    = value;
            _ledIndicatorColorSet = true;
        }
    }

    /// <summary>
    /// When <c>true</c>, requests the daemon to append per-request timing
    /// metrics to every response during this session.
    ///
    /// The daemon will include a <c>"_m"</c> object in every <c>"t":0</c>
    /// response envelope with millisecond-resolution durations for each
    /// processing phase:
    /// <code>
    /// "_m": { "pr": 1, "dp": 0, "ex": 3, "sr": 0, "tt": 4 }
    /// </code>
    /// <list type="table">
    ///   <listheader><term>Key</term><description>Phase</description></listheader>
    ///   <item><term>pr</term><description>JSON parse (extract "c" and "i")</description></item>
    ///   <item><term>dp</term><description>Dispatch (bounds-check + resource pre-check)</description></item>
    ///   <item><term>ex</term><description>Execute (handler, including arg parsing + HW work)</description></item>
    ///   <item><term>sr</term><description>Serialize (response envelope formatting)</description></item>
    ///   <item><term>tt</term><description>Total end-to-end (entry to rpc_dispatch through cdc_send)</description></item>
    /// </list>
    ///
    /// The metrics are available in <see cref="RpcLogEntry.RawJson"/> via
    /// <see cref="FlipperZero.NET.Abstractions.IRpcDiagnostics"/>.
    ///
    /// This flag is scoped to a single connection lifecycle and is reset to
    /// <c>false</c> by the daemon on every disconnect.
    ///
    /// Defaults to <c>false</c> (no metrics overhead).
    /// </summary>
    public bool DaemonDiagnostics { get; init; }

    /// <summary>
    /// When <c>true</c>, the <see cref="HeartbeatTransport"/> keep-alive layer is
    /// omitted from the transport chain.  No keep-alive frames are sent by the client
    /// and no inbound-silence timeout is enforced on the client side.
    ///
    /// To prevent the daemon from timing out the client (which still runs its own RX
    /// watchdog), the <c>configure</c> command is sent with very large heartbeat and
    /// timeout values (effectively infinite) so the daemon never considers the host gone.
    /// The daemon will still send its own keep-alive frames; the client reader loop
    /// silently discards them.
    ///
    /// Use this in environments where the heartbeat background loop is harmful — for
    /// example, single-threaded Blazor WASM, where the timer loop competes with the
    /// WebSerial read pump on the cooperative scheduler and can cause false disconnects.
    /// In a browser environment the user is present and will notice connection loss
    /// immediately, so transport-level liveness probing is unnecessary.
    ///
    /// Defaults to <c>false</c> (heartbeat enabled).
    /// </summary>
    public bool DisableHeartbeat { get; init; }

    /// <summary>
    /// When <c>true</c>, the <see cref="PacketSerializationTransport"/> single-writer
    /// serialisation layer is omitted from the transport chain.  Outbound writes go
    /// directly to the underlying transport without going through a bounded channel and
    /// a background writer loop.
    ///
    /// The serialisation layer exists solely to provide a single-writer guarantee when
    /// multiple threads call <see cref="FlipperRpcClient.SendAsync{TCommand,TResponse}"/>
    /// concurrently.  In single-threaded environments (e.g. Blazor WASM) there is no
    /// thread-level concurrency, so the layer adds overhead — an extra cooperative
    /// <c>Task.Run</c> loop — without providing any benefit.
    ///
    /// When this flag is set the caller is responsible for not invoking
    /// <c>SendAsync</c> concurrently from multiple threads.
    ///
    /// Defaults to <c>false</c> (serialisation enabled).
    /// </summary>
    public bool DisablePacketSerialization { get; init; }
}
