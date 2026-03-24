using DolphinLink.Client.Transport;

namespace DolphinLink.Tests.Infrastructure;

/// <summary>
/// xUnit collection fixture that opens a single <see cref="RpcClient"/>
/// for the duration of the entire test collection, then disposes it afterwards.
///
/// Configuration
/// -------------
/// Set the <c>FLIPPER_PORT</c> environment variable to the COM port before
/// running tests, e.g.:
///   Windows : set FLIPPER_PORT=COM3
///   Linux   : export FLIPPER_PORT=/dev/ttyACM0
///
/// If the variable is not set, or the device cannot be reached,
/// <see cref="IsAvailable"/> is <c>false</c> and all tests that depend on this
/// fixture will skip via <see cref="RequiresDeviceFact"/> or
/// <see cref="RequiresDeviceTheory"/>.
/// </summary>
public sealed class DeviceFixture : IAsyncLifetime
{
    public const string EnvVar = "FLIPPER_PORT";

    private const string SkipReason =
        $"Flipper not available. Set {EnvVar} to a valid port and ensure the device is connected.";

    /// <summary>
    /// <c>true</c> when <c>FLIPPER_PORT</c> is set and the client connected
    /// successfully.
    /// </summary>
    public bool IsAvailable { get; private set; }

    /// <summary>
    /// The port name read from the environment, or <c>null</c> if not set.
    /// </summary>
    public string? PortName { get; private set; }

    /// <summary>
    /// The connected client. Accessing this property skips the current test
    /// (via <see cref="Skip"/>) when <see cref="IsAvailable"/> is <c>false</c>.
    /// </summary>
    public RpcClient Client
    {
        get
        {
            Skip.IfNot(IsAvailable, SkipReason);
            return _client!;
        }
    }

    private RpcClient? _client;

    public async Task InitializeAsync()
    {
        PortName = Environment.GetEnvironmentVariable(EnvVar);

        if (string.IsNullOrWhiteSpace(PortName))
        {
            // No port configured — tests will be skipped.
            //return;
            PortName = "COM4";
        }

        try
        {
            _client = new RpcClient(new SerialPortTransport(PortName));
            await _client.ConnectAsync().ConfigureAwait(false);
            IsAvailable = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or InvalidOperationException)
        {
            // Port exists in env var but device is unavailable (wrong port, unplugged, etc.)
            // Tests will be skipped rather than crashing the entire collection.
            IsAvailable = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }
    }

    // IAsyncLifetime requires both sync and async dispose; delegate to async.
    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();
}

/// <summary>
/// Provides the shared collection name used by hardware and manual test
/// assemblies. Each test assembly must declare its own
/// <c>[CollectionDefinition(DeviceCollection.Name)]</c> so xUnit can
/// discover the fixture registration within that assembly.
/// </summary>
public static class DeviceCollection
{
    public const string Name = "Flipper integration";
}

/// <summary>
/// Provides constants for the bootstrapper hardware test collection.
///
/// Bootstrap tests need exclusive access to both CDC interfaces:
/// <list type="bullet">
///   <item><c>FLIPPER_SYSTEM_PORT</c> — CDC interface 0 (native protobuf RPC, used by qFlipper)</item>
///   <item><c>FLIPPER_PORT</c> — CDC interface 1 (daemon NDJSON port)</item>
/// </list>
/// They run in their own collection so they do not contend with the shared
/// <see cref="DeviceFixture"/> that holds CDC interface 1 open.
/// </summary>
public static class BootstrapCollection
{
    public const string Name = "Flipper bootstrap";

    /// <summary>
    /// Environment variable that specifies the serial port for CDC interface 0
    /// (the Flipper's native protobuf RPC port, also used by qFlipper).
    /// Example: <c>COM3</c> on Windows, <c>/dev/ttyACM0</c> on Linux.
    /// </summary>
    public const string SystemPortEnvVar = "FLIPPER_SYSTEM_PORT";
}
