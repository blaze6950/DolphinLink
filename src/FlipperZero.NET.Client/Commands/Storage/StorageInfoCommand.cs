using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Storage;

/// <summary>
/// Returns filesystem capacity and free space for a storage path.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"storage_info","path":"/ext"}</code>
///
/// Wire format (response):
/// <code>{"id":N,"status":"ok","data":{"path":"/ext","total_kb":30000,"free_kb":25000}}</code>
///
/// Supported paths: <c>"/int"</c> (internal flash), <c>"/ext"</c> (SD card).
/// </summary>
public readonly struct StorageInfoCommand : IRpcCommand<StorageInfoResponse>
{
    /// <param name="path">Storage root, e.g. <c>"/int"</c> or <c>"/ext"</c>.</param>
    public StorageInfoCommand(string path) => Path = path;

    /// <summary>Storage path to query.</summary>
    public string Path { get; }

    /// <inheritdoc />
    public string CommandName => "storage_info";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteString("path", Path);
    }
}

/// <summary>Response to <see cref="StorageInfoCommand"/>.</summary>
public readonly struct StorageInfoResponse : IRpcCommandResponse
{
    /// <summary>Queried path.</summary>
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    /// <summary>Total filesystem capacity in kibibytes.</summary>
    [JsonPropertyName("total_kb")]
    public uint TotalKb { get; init; }

    /// <summary>Free space in kibibytes.</summary>
    [JsonPropertyName("free_kb")]
    public uint FreeKb { get; init; }
}
