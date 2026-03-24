using System.Text.Json.Serialization;
using DolphinLink.Client.Abstractions;
using DolphinLink.Client.Converters;

namespace DolphinLink.Client.Commands.Storage;

/// <summary>A single file or directory entry returned by <see cref="StorageListCommand"/>.</summary>
public readonly struct StorageEntry : IRpcCommandResponse
{
    /// <summary>File or directory name (without path).</summary>
    [JsonPropertyName("nm")]
    public string? Name { get; init; }

    /// <summary><c>true</c> if this entry is a directory.</summary>
    [JsonConverter(typeof(NumericBoolJsonConverter))]
    [JsonPropertyName("d")]
    public bool IsDir { get; init; }

    /// <summary>File size in bytes (0 for directories).</summary>
    [JsonPropertyName("sz")]
    public uint Size { get; init; }
}

/// <summary>Response to <see cref="StorageListCommand"/>.</summary>
public readonly partial struct StorageListResponse : IRpcCommandResponse
{
    /// <summary>Array of directory entries, or <c>null</c> if the directory is empty.</summary>
    [JsonPropertyName("en")]
    public StorageEntry[]? Entries { get; init; }
}
