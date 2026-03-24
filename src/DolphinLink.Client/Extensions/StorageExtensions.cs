using DolphinLink.Client.Commands.Storage;

namespace DolphinLink.Client.Extensions;

/// <summary>
/// Hand-written extension methods for storage commands that require custom
/// argument construction or response unwrapping not suited to code generation.
/// </summary>
public static partial class StorageExtensions
{
    /// <summary>
    /// Lists the contents of a directory on the Flipper's storage.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="path">The absolute path of the directory to list (e.g. <c>/int</c>, <c>/ext</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The list response containing directory entries.</returns>
    public static Task<StorageListResponse> StorageListAsync(
        this RpcClient client,
        string path,
        CancellationToken ct = default)
        => client.SendAsync<StorageListCommand, StorageListResponse>(
            new StorageListCommand { Path = path }, ct);
}
