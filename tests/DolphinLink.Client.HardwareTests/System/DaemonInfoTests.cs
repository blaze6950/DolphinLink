using DolphinLink.Client.Commands.System;

namespace DolphinLink.Client.HardwareTests.System;

/// <summary>
/// Integration tests for <see cref="Extensions.SystemExtensions.DaemonInfoAsync"/>.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~DaemonInfoTests"
/// </summary>
[Collection(DeviceCollection.Name)]
public sealed class DaemonInfoTests(DeviceFixture fixture)
{
    private RpcClient Client => fixture.Client;

    /// <summary>
    /// <see cref="Extensions.SystemExtensions.DaemonInfoAsync"/> must return the
    /// canonical daemon name <c>"dolphin_link_rpc_daemon"</c>.
    /// Validates: identity check used by <see cref="RpcClient.ConnectAsync"/>
    /// to confirm the correct FAP is running.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresDeviceFact]
    public async Task DaemonInfo_ReturnsCorrectName()
    {
        var info = await Client.DaemonInfoAsync();

        Assert.Equal("dolphin_link_rpc_daemon", info.Name);
    }

    /// <summary>
    /// The reported protocol version must be a positive integer.
    /// Validates: <see cref="DaemonInfoResponse.Version"/> is populated and
    /// the daemon was built with a valid <c>DAEMON_PROTOCOL_VERSION</c>.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresDeviceFact]
    public async Task DaemonInfo_ReturnsPositiveVersion()
    {
        var info = await Client.DaemonInfoAsync();

        Assert.True(info.Version >= 1,
            $"Protocol version must be >= 1, got {info.Version}");
    }

    /// <summary>
    /// The returned commands list must be non-null and non-empty.
    /// Validates: <see cref="DaemonInfoResponse.Commands"/> is populated with
    /// at least the core commands.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresDeviceFact]
    public async Task DaemonInfo_ReturnsNonEmptyCommandsList()
    {
        var info = await Client.DaemonInfoAsync();

        Assert.NotNull(info.Commands);
        Assert.NotEmpty(info.Commands);
    }

    /// <summary>
    /// <see cref="DaemonInfoResponse.Supports(string)"/> must return <c>true</c>
    /// for a command that every daemon version supports (ping).
    /// Validates: the commands array contains core commands and the
    /// <see cref="DaemonInfoResponse.Supports(string)"/> helper works correctly.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresDeviceFact]
    public async Task DaemonInfo_SupportsKnownCommand()
    {
        var info = await Client.DaemonInfoAsync();

        Assert.True(info.Supports("ping"),
            "Expected daemon to report support for 'ping'");
    }

    /// <summary>
    /// <see cref="DaemonInfoResponse.Supports(string)"/> must return <c>false</c>
    /// for a command name that does not exist on the wire.
    /// Validates: the linear search in <see cref="DaemonInfoResponse.Supports(string)"/>
    /// correctly returns false for absent entries.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresDeviceFact]
    public async Task DaemonInfo_DoesNotSupportBogusCommand()
    {
        var info = await Client.DaemonInfoAsync();

        Assert.False(info.Supports("nonexistent_command_xyz"),
            "Expected Supports to return false for an unknown command");
    }

    /// <summary>
    /// Calling <see cref="Extensions.SystemExtensions.DaemonInfoAsync"/> multiple
    /// times must return consistent values.
    /// Validates: idempotency — daemon identity and version are read-only and
    /// must be stable across repeated round-trips.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresDeviceFact]
    public async Task DaemonInfo_MultipleCalls_ReturnConsistentValues()
    {
        var first = await Client.DaemonInfoAsync();
        var second = await Client.DaemonInfoAsync();

        Assert.Equal(first.Name, second.Name);
        Assert.Equal(first.Version, second.Version);
        Assert.Equal(first.Commands?.Length, second.Commands?.Length);
    }
}
