using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Storage;

/// <summary>
/// Creates a directory on the Flipper filesystem.
/// Intermediate directories must exist; only the final component is created.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"storage_mkdir","path":"/ext/mydir"}</code>
///
/// Wire format (response):
/// <code>{"t":0,"i":N}</code>
/// </summary>
public readonly struct StorageMkdirCommand : IRpcCommand<StorageMkdirResponse>
{
    /// <param name="path">Directory path to create, e.g. <c>"/ext/mydir"</c>.</param>
    public StorageMkdirCommand(string path) => Path = path;

    /// <summary>Directory path to create.</summary>
    public string Path { get; }

    /// <inheritdoc />
    public string CommandName => "storage_mkdir";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteString("path", Path);
    }
}

/// <summary>Response to <see cref="StorageMkdirCommand"/>.</summary>
public readonly struct StorageMkdirResponse : IRpcCommandResponse { }
