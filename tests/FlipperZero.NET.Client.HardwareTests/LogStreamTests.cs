using FlipperZero.NET.Abstractions;
using FlipperZero.NET.Extensions;
using FlipperZero.NET.Transport;

namespace FlipperZero.NET.Client.HardwareTests;

/// <summary>
/// Hardware tests for client-side diagnostics via <see cref="IRpcDiagnostics"/> injection.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~LogStreamTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class LogStreamTests(FlipperFixture fixture)
{
    /// <summary>
    /// A capturing <see cref="IRpcDiagnostics"/> used as an injected sink in tests
    /// that need their own <see cref="FlipperRpcClient"/> instance.
    /// </summary>
    private sealed class CapturingSink : IRpcDiagnostics
    {
        private readonly List<RpcLogEntry> _entries = [];
        public IReadOnlyList<RpcLogEntry> Entries => _entries;
        public void Log(RpcLogEntry entry) => _entries.Add(entry);
    }

    /// <summary>
    /// A ping round-trip must produce at least one <see cref="RpcLogKind.CommandSent"/>
    /// and one <see cref="RpcLogKind.ResponseReceived"/> entry via the injected
    /// <see cref="IRpcDiagnostics"/> sink, with the expected field values
    /// and a positive round-trip time.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task Diagnostics_PingProducesExpectedEntries()
    {
        Skip.If(fixture.PortName is null);

        var sink = new CapturingSink();
        await using var client = new FlipperRpcClient(new SerialPortTransport(fixture.PortName!), diagnostics: sink);
        await client.ConnectAsync();

        await client.PingAsync();

        var sent = sink.Entries.FirstOrDefault(e =>
            e is { Kind: RpcLogKind.CommandSent, CommandName: "ping" });
        var received = sink.Entries.FirstOrDefault(e =>
            e is { Kind: RpcLogKind.ResponseReceived, Status: "ok" });

        // CommandSent entry
        Assert.Equal(RpcLogSource.Client, sent.Source);
        Assert.NotNull(sent.RawJson);
        Assert.True(sent.Elapsed > TimeSpan.Zero, "CommandSent.Elapsed must be positive");
        Assert.Null(sent.RoundTrip);

        // ResponseReceived entry
        Assert.Equal(RpcLogSource.Client, received.Source);
        Assert.NotNull(received.RawJson);
        Assert.True(received.Elapsed > TimeSpan.Zero, "ResponseReceived.Elapsed must be positive");
        Assert.NotNull(received.RoundTrip);
        Assert.True(received.RoundTrip!.Value > TimeSpan.Zero, "RoundTrip must be positive");

        // The request IDs must match
        Assert.Equal(sent.RequestId, received.RequestId);
    }

    /// <summary>
    /// A client created without a diagnostics sink (no-op path) must still execute
    /// commands successfully — verifies the JIT-eliminated logging path does not fault.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task Diagnostics_NullSink_CommandSucceeds()
    {
        Skip.If(fixture.PortName is null);

        // Construct without diagnostics — uses the no-op NullDiagnostics singleton.
        await using var client = new FlipperRpcClient(new SerialPortTransport(fixture.PortName!));
        await client.ConnectAsync();

        // Must not throw; the null-sink path is exercised.
        await client.PingAsync();
    }

    /// <summary>
    /// The injected sink receives entries for every command sent during the
    /// lifetime of the client, covering multiple sequential commands.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task Diagnostics_MultipleCommands_AllEntriesCaptured()
    {
        Skip.If(fixture.PortName is null);

        var sink = new CapturingSink();
        await using var client = new FlipperRpcClient(new SerialPortTransport(fixture.PortName!), diagnostics: sink);
        await client.ConnectAsync();

        await client.PingAsync();
        await client.PingAsync();

        var sentCount = sink.Entries.Count(e =>
            e is { Kind: RpcLogKind.CommandSent, CommandName: "ping" });

        Assert.Equal(2, sentCount);
    }
}
