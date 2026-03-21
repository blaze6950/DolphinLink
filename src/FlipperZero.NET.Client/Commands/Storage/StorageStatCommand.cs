using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Storage;

/// <summary>
/// Returns metadata for a file or directory on the Flipper filesystem.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"storage_stat","path":"/ext/test.txt"}</code>
///
/// Wire format (response):
/// <code>{"t":0,"i":N,"p":{"size":42,"is_dir":false}}</code>
/// </summary>
public readonly struct StorageStatCommand : IRpcCommand<StorageStatResponse>
{
    /// <param name="path">Path to stat, e.g. <c>"/ext/test.txt"</c>.</param>
    public StorageStatCommand(string path) => Path = path;

    /// <summary>Path to query metadata for.</summary>
    public string Path { get; }

    /// <inheritdoc />
    public string CommandName => "storage_stat";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteString("path", Path);
    }
}

/// <summary>Response to <see cref="StorageStatCommand"/>.</summary>
public readonly struct StorageStatResponse : IRpcCommandResponse
{
    /// <summary>File size in bytes (0 for directories).</summary>
    [JsonPropertyName("size")]
    public uint Size { get; init; }

    /// <summary><c>true</c> if the path points to a directory.</summary>
    [JsonPropertyName("is_dir")]
    public bool IsDir { get; init; }
}
