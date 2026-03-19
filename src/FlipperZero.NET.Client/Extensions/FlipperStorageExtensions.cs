using FlipperZero.NET.Commands.Storage;

namespace FlipperZero.NET.Extensions;

/// <summary>
/// Extension methods on <see cref="FlipperRpcClient"/> for filesystem storage commands.
/// </summary>
public static class FlipperStorageExtensions
{
    /// <summary>
    /// Returns filesystem capacity and free space for a storage root.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="path">Storage root, e.g. <c>"/int"</c> or <c>"/ext"</c>.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<StorageInfoResponse> StorageInfoAsync(
        this FlipperRpcClient client,
        string path,
        CancellationToken ct = default)
        => client.SendAsync<StorageInfoCommand, StorageInfoResponse>(new StorageInfoCommand(path), ct);

    /// <summary>Lists the contents of a directory on the Flipper.</summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="path">Absolute directory path on the Flipper filesystem.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<StorageListResponse> StorageListAsync(
        this FlipperRpcClient client,
        string path,
        CancellationToken ct = default)
        => client.SendAsync<StorageListCommand, StorageListResponse>(new StorageListCommand(path), ct);

    /// <summary>
    /// Reads a file from the Flipper.
    /// The response <see cref="StorageReadResponse.Data"/> contains the raw file bytes.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="path">Absolute file path on the Flipper filesystem.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<StorageReadResponse> StorageReadAsync(
        this FlipperRpcClient client,
        string path,
        CancellationToken ct = default)
        => client.SendAsync<StorageReadCommand, StorageReadResponse>(new StorageReadCommand(path), ct);

    /// <summary>
    /// Writes data to a file on the Flipper (creates or overwrites).
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="path">Absolute destination path on the Flipper filesystem.</param>
    /// <param name="data">Raw file content to write.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<StorageWriteResponse> StorageWriteAsync(
        this FlipperRpcClient client,
        string path, byte[] data,
        CancellationToken ct = default)
        => client.SendAsync<StorageWriteCommand, StorageWriteResponse>(new StorageWriteCommand(path, data), ct);

    /// <summary>Creates a directory on the Flipper filesystem.</summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="path">Absolute path for the new directory.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<StorageMkdirResponse> StorageMkdirAsync(
        this FlipperRpcClient client,
        string path,
        CancellationToken ct = default)
        => client.SendAsync<StorageMkdirCommand, StorageMkdirResponse>(new StorageMkdirCommand(path), ct);

    /// <summary>Removes a file or empty directory from the Flipper filesystem.</summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="path">Absolute path to remove.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<StorageRemoveResponse> StorageRemoveAsync(
        this FlipperRpcClient client,
        string path,
        CancellationToken ct = default)
        => client.SendAsync<StorageRemoveCommand, StorageRemoveResponse>(new StorageRemoveCommand(path), ct);

    /// <summary>Returns metadata (size and is_dir flag) for a path on the Flipper.</summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="path">Absolute path to stat.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<StorageStatResponse> StorageStatAsync(
        this FlipperRpcClient client,
        string path,
        CancellationToken ct = default)
        => client.SendAsync<StorageStatCommand, StorageStatResponse>(new StorageStatCommand(path), ct);
}
