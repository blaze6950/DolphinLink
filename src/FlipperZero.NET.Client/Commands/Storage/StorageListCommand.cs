using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Storage;

/// <summary>
/// Lists the contents of a directory on the Flipper storage.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"storage_list","path":"/ext/apps"}</code>
///
/// Wire format (response):
/// <code>{"t":0,"i":N,"p":{"entries":[{"name":"MyApp.fap","is_dir":false,"size":12345}]}}</code>
/// </summary>
public readonly struct StorageListCommand : IRpcCommand<StorageListResponse>
{
    /// <param name="path">Directory path to list, e.g. <c>"/ext/apps"</c>.</param>
    public StorageListCommand(string path) => Path = path;

    /// <summary>Directory path to list.</summary>
    public string Path { get; }

    /// <inheritdoc />
    public string CommandName => "storage_list";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteString("path", Path);
    }
}

/// <summary>A single file or directory entry returned by <see cref="StorageListCommand"/>.</summary>
public readonly struct StorageEntry : IRpcCommandResponse
{
    /// <summary>File or directory name (without path).</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary><c>true</c> if this entry is a directory.</summary>
    [JsonPropertyName("is_dir")]
    public bool IsDir { get; init; }

    /// <summary>File size in bytes (0 for directories).</summary>
    [JsonPropertyName("size")]
    public uint Size { get; init; }
}

/// <summary>Response to <see cref="StorageListCommand"/>.</summary>
public readonly struct StorageListResponse : IRpcCommandResponse
{
    /// <summary>Array of directory entries, or <c>null</c> if the directory is empty.</summary>
    [JsonPropertyName("entries")]
    public StorageEntry[]? Entries { get; init; }
}
