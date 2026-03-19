using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Storage;

/// <summary>
/// Removes a file or empty directory from the Flipper filesystem.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"storage_remove","path":"/ext/test.txt"}</code>
///
/// Wire format (response):
/// <code>{"id":N,"status":"ok"}</code>
///
/// Non-empty directories are not removed; use recursive deletion on the host if needed.
/// </summary>
public readonly struct StorageRemoveCommand : IRpcCommand<StorageRemoveResponse>
{
    /// <param name="path">Path of the file or empty directory to remove.</param>
    public StorageRemoveCommand(string path) => Path = path;

    /// <summary>Path to remove.</summary>
    public string Path { get; }

    /// <inheritdoc />
    public string CommandName => "storage_remove";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteString("path", Path);
    }
}

/// <summary>Response to <see cref="StorageRemoveCommand"/>.</summary>
public readonly struct StorageRemoveResponse : IRpcCommandResponse { }
