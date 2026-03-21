using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Core;

/// <summary>
/// Closes an open stream by its numeric id.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"stream_close","stream":M}</code>
///
/// Wire format (response):
/// <code>{"t":0,"i":N}</code>
///
/// This command is called automatically by <see cref="RpcStream{TEvent}.DisposeAsync"/>;
/// callers should prefer disposing the stream handle rather than invoking this directly.
/// </summary>
public readonly struct StreamCloseCommand : IRpcCommand<StreamCloseResponse>
{
    /// <param name="streamId">The stream id to close (maps to <c>"stream"</c> in JSON).</param>
    public StreamCloseCommand(uint streamId) => StreamId = streamId;

    /// <summary>The numeric stream id to close.</summary>
    public uint StreamId { get; }

    /// <inheritdoc />
    public string CommandName => "stream_close";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteNumber("stream", StreamId);
    }
}

/// <summary>Response to <see cref="StreamCloseCommand"/>.</summary>
public readonly struct StreamCloseResponse : IRpcCommandResponse { }
