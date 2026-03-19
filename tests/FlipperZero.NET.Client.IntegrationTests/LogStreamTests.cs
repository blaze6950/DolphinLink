using FlipperZero.NET.Client.IntegrationTests.Infrastructure;
using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.IntegrationTests;

/// <summary>
/// Integration tests for client-side log streaming via <see cref="FlipperRpcClient.OnLogEntry"/>.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~LogStreamTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class LogStreamTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    // -----------------------------------------------------------------------
    // Client log event
    // -----------------------------------------------------------------------

    /// <summary>
    /// A ping round-trip must produce at least one <see cref="RpcLogKind.CommandSent"/>
    /// and one <see cref="RpcLogKind.ResponseReceived"/> entry via
    /// <see cref="FlipperRpcClient.OnLogEntry"/>, with the expected field values
    /// and a positive round-trip time.
    /// </summary>
    [RequiresFlipperFact]
    public async Task OnLogEntry_PingProducesExpectedEntries()
    {
        var entries = new List<RpcLogEntry>();
        Action<RpcLogEntry> handler = e => entries.Add(e);

        Client.OnLogEntry += handler;
        try
        {
            await Client.PingAsync();
        }
        finally
        {
            Client.OnLogEntry -= handler;
        }

        var sent = entries.FirstOrDefault(e =>
            e is { Kind: RpcLogKind.CommandSent, CommandName: "ping" });
        var received = entries.FirstOrDefault(e =>
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
    /// Two independently registered handlers must each receive entries for the
    /// same command — validates multicast delegate fan-out.
    /// </summary>
    [RequiresFlipperFact]
    public async Task OnLogEntry_MultipleHandlers_BothReceiveEntries()
    {
        var firstEntries = new List<RpcLogEntry>();
        var secondEntries = new List<RpcLogEntry>();

        Action<RpcLogEntry> first = e => firstEntries.Add(e);
        Action<RpcLogEntry> second = e => secondEntries.Add(e);

        Client.OnLogEntry += first;
        Client.OnLogEntry += second;
        try
        {
            await Client.PingAsync();
        }
        finally
        {
            Client.OnLogEntry -= first;
            Client.OnLogEntry -= second;
        }

        Assert.Contains(firstEntries, e =>
            e is { Kind: RpcLogKind.CommandSent, CommandName: "ping" });
        Assert.Contains(secondEntries, e =>
            e is { Kind: RpcLogKind.CommandSent, CommandName: "ping" });
    }

    /// <summary>
    /// After unsubscribing, the handler must not receive entries for subsequent
    /// commands.
    /// </summary>
    [RequiresFlipperFact]
    public async Task OnLogEntry_UnsubscribeStopsDelivery()
    {
        var entries = new List<RpcLogEntry>();
        Action<RpcLogEntry> handler = e => entries.Add(e);

        Client.OnLogEntry += handler;
        Client.OnLogEntry -= handler;

        // Command sent after unsubscribe — handler must not be called.
        await Client.PingAsync();

        Assert.Empty(entries);
    }
}
