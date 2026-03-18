using FlipperZero.NET;

namespace FlipperZero.NET.Client.IntegrationTests;

/// <summary>
/// xUnit collection for tests that open their own <see cref="FlipperRpcClient"/>
/// instances.  These tests require exclusive access to the serial port so they
/// CANNOT share the <see cref="FlipperCollection"/> fixture (which holds the
/// port open for the duration of that collection).
///
/// xUnit runs collections sequentially, so <see cref="FlipperCollection"/>
/// finishes (and the fixture disposes the port) before this collection starts.
/// </summary>
[CollectionDefinition(Name)]
public sealed class LifecycleCollection
{
    public const string Name = "Flipper lifecycle";
}

/// <summary>
/// Integration tests verifying that <see cref="FlipperRpcClient"/> disposes
/// cleanly under various conditions.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~LifecycleTests"
/// </summary>
[Collection(LifecycleCollection.Name)]
public sealed class LifecycleTests
{
    private readonly string _portName;

    public LifecycleTests()
    {
        _portName = Environment.GetEnvironmentVariable(FlipperFixture.EnvVar)
            ?? string.Empty;
    }

    /// <summary>
    /// A freshly created and immediately disposed client must not throw.
    /// Validates: dispose on a client that was never connected.
    /// </summary>
    [RequiresFlipperFact]
    public async Task Dispose_BeforeConnect_DoesNotThrow()
    {
        var client = new FlipperRpcClient(_portName);
        await client.DisposeAsync(); // Should be a no-op
    }

    /// <summary>
    /// A client that is connected, sends a ping, then is disposed must not
    /// throw during disposal.
    /// Validates: graceful shutdown of both background loops.
    /// </summary>
    [RequiresFlipperFact]
    public async Task Dispose_AfterSuccessfulPing_DoesNotThrow()
    {
        await using var client = new FlipperRpcClient(_portName);
        client.Connect();

        var pong = await client.PingAsync();
        Assert.True(pong);
    }

    /// <summary>
    /// Disposing a client that has an open IR receive stream must close the
    /// stream cleanly (the daemon releases the resource) and not throw.
    /// Validates: <see cref="FlipperRpcClient.DisposeAsync"/> while an IR
    /// stream is in-flight.
    /// </summary>
    [RequiresFlipperFact]
    public async Task Dispose_WithOpenIrStream_DoesNotThrow()
    {
        await using var client = new FlipperRpcClient(_portName);
        client.Connect();

        // Open an IR receive stream — do NOT dispose it; let the client do it.
        var stream = await client.IrReceiveStartAsync();
        _ = stream; // intentionally leaked to test client-side cleanup
    }

    /// <summary>
    /// Disposing a client that has an open GPIO watch stream must close the
    /// stream cleanly (the daemon releases the stream slot) and not throw.
    /// Validates: <see cref="FlipperRpcClient.DisposeAsync"/> while a GPIO
    /// stream is in-flight.
    /// </summary>
    [RequiresFlipperFact]
    public async Task Dispose_WithOpenGpioStream_DoesNotThrow()
    {
        await using var client = new FlipperRpcClient(_portName);
        client.Connect();

        // Open a GPIO watch stream — do NOT dispose it; let the client do it.
        var stream = await client.GpioWatchStartAsync("6");
        _ = stream; // intentionally leaked to test client-side cleanup
    }

    /// <summary>
    /// Calling <see cref="FlipperRpcClient.DisposeAsync"/> more than once must
    /// be safe (idempotent).
    /// </summary>
    [RequiresFlipperFact]
    public async Task Dispose_CalledTwice_DoesNotThrow()
    {
        var client = new FlipperRpcClient(_portName);
        client.Connect();
        await client.PingAsync();

        await client.DisposeAsync();
        await client.DisposeAsync(); // second call must be harmless
    }
}
