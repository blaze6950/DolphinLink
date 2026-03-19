using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.IntegrationTests.Infrastructure;

/// <summary>
/// xUnit collection fixture that opens a single <see cref="FlipperRpcClient"/>
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
/// fixture will skip via <see cref="RequiresFlipperFact"/> or
/// <see cref="RequiresFlipperTheory"/>.
/// </summary>
public sealed class FlipperFixture : IAsyncLifetime
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
    public FlipperRpcClient Client
    {
        get
        {
            Skip.IfNot(IsAvailable, SkipReason);
            return _client!;
        }
    }

    private FlipperRpcClient? _client;

    public async Task InitializeAsync()
    {
        PortName = Environment.GetEnvironmentVariable(EnvVar);

        if (string.IsNullOrWhiteSpace(PortName))
        {
            // No port configured — tests will be skipped.
            return;
        }

        try
        {
            _client = new FlipperRpcClient(PortName);
            await _client.ConnectAsync().ConfigureAwait(false);

            // Verify connectivity with a ping before running any tests.
            var pong = await _client.PingAsync(CancellationToken.None).ConfigureAwait(false);
            IsAvailable = pong;
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
/// Marks the test collection that shares the <see cref="FlipperFixture"/>.
/// All integration-test classes must be decorated with
/// <c>[Collection(FlipperCollection.Name)]</c>.
/// </summary>
[CollectionDefinition(Name)]
public sealed class FlipperCollection : ICollectionFixture<FlipperFixture>
{
    public const string Name = "Flipper integration";
}
