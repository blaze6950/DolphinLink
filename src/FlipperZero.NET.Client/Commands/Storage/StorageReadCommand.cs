using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;
using FlipperZero.NET.Converters;

namespace FlipperZero.NET.Commands.Storage;

/// <summary>
/// Reads a file from the Flipper filesystem.
/// File content is returned Base64-encoded in the <c>"data"</c> field and
/// decoded transparently to a <c>byte[]</c>.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"storage_read","path":"/ext/test.txt"}</code>
///
/// Wire format (response):
/// <code>{"id":N,"status":"ok","data":{"data":"SGVsbG8gV29ybGQ="}}</code>
///
/// The daemon reads the file in fragments of up to 128 bytes each; for files
/// larger than one fragment the daemon sends multiple JSON lines with the same
/// request id before the final <c>{"id":N,"status":"ok"}</c>.  The C# client
/// concatenates the fragments automatically.
/// </summary>
public readonly struct StorageReadCommand : IRpcCommand<StorageReadResponse>
{
    /// <param name="path">Absolute path to the file to read, e.g. <c>"/ext/test.txt"</c>.</param>
    public StorageReadCommand(string path) => Path = path;

    /// <summary>Absolute path to the file.</summary>
    public string Path { get; }

    /// <inheritdoc />
    public string CommandName => "storage_read";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteString("path", Path);
    }
}

/// <summary>Response to <see cref="StorageReadCommand"/>.</summary>
public readonly struct StorageReadResponse : IRpcCommandResponse
{
    /// <summary>Complete file contents as raw bytes (decoded from the Base64 wire representation).</summary>
    [JsonPropertyName("data")]
    [JsonConverter(typeof(Base64JsonConverter))]
    public byte[]? Data { get; init; }
}
