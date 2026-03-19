using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Storage;

/// <summary>
/// Writes data to a file on the Flipper filesystem, creating it if it does not exist
/// or overwriting it if it does.  File content is supplied as a raw <c>byte[]</c>
/// and encoded to Base64 automatically before transmission.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"storage_write","path":"/ext/test.txt","data":"SGVsbG8gV29ybGQ="}</code>
///
/// Wire format (response):
/// <code>{"id":N,"status":"ok"}</code>
/// </summary>
public readonly struct StorageWriteCommand : IRpcCommand<StorageWriteResponse>
{
    /// <param name="path">Destination path, e.g. <c>"/ext/test.txt"</c>.</param>
    /// <param name="data">File content as raw bytes.</param>
    public StorageWriteCommand(string path, byte[] data)
    {
        Path = path;
        Data = data;
    }

    /// <summary>Destination path on the Flipper filesystem.</summary>
    public string Path { get; }

    /// <summary>File content as raw bytes.</summary>
    public byte[] Data { get; }

    /// <inheritdoc />
    public string CommandName => "storage_write";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteString("path", Path);
        writer.WriteString("data", Convert.ToBase64String(Data));
    }
}

/// <summary>Response to <see cref="StorageWriteCommand"/>.</summary>
public readonly struct StorageWriteResponse : IRpcCommandResponse { }
