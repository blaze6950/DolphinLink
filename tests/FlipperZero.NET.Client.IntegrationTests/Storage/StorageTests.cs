using System.Text;
using FlipperZero.NET.Client.IntegrationTests.Infrastructure;
using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.IntegrationTests.Storage;

/// <summary>
/// Integration tests for storage commands:
/// <see cref="FlipperRpcClient.StorageInfoAsync"/>,
/// <see cref="FlipperRpcClient.StorageListAsync"/>,
/// <see cref="FlipperRpcClient.StorageReadAsync"/>,
/// <see cref="FlipperRpcClient.StorageWriteAsync"/>,
/// <see cref="FlipperRpcClient.StorageMkdirAsync"/>,
/// <see cref="FlipperRpcClient.StorageRemoveAsync"/>, and
/// <see cref="FlipperRpcClient.StorageStatAsync"/>.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~StorageTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class StorageTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    // Scratch directory created and removed within tests.
    private const string ScratchDir = "/ext/rpc_test_scratch";
    private const string ScratchFile = "/ext/rpc_test_scratch/hello.txt";

    // -----------------------------------------------------------------------
    // storage_info
    // -----------------------------------------------------------------------

    /// <summary>
    /// Querying storage info for <c>/int</c> must return a non-zero total
    /// capacity and a free count no larger than total.
    /// Validates: <c>storage_info</c> happy-path with the internal flash.
    /// </summary>
    [RequiresFlipperFact]
    public async Task StorageInfo_InternalStorage_ReturnsPlausibleCapacity()
    {
        var info = await Client.StorageInfoAsync("/int");

        Assert.True(info.TotalKb > 0, "TotalKb must be positive");
        Assert.True(info.FreeKb <= info.TotalKb, "FreeKb must not exceed TotalKb");
    }

    /// <summary>
    /// Querying storage info for <c>/ext</c> (SD card) must succeed and return
    /// plausible values.
    /// Validates: <c>storage_info</c> happy-path with the external SD card.
    /// </summary>
    [RequiresFlipperFact]
    public async Task StorageInfo_ExternalStorage_ReturnsPlausibleCapacity()
    {
        var info = await Client.StorageInfoAsync("/ext");

        Assert.True(info.TotalKb > 0, "TotalKb must be positive");
        Assert.True(info.FreeKb <= info.TotalKb, "FreeKb must not exceed TotalKb");
    }

    /// <summary>
    /// Querying storage info for a non-existent path must throw a
    /// <see cref="FlipperRpcException"/> with the <c>storage_error</c>
    /// error code.
    /// Validates: error path in the <c>storage_info</c> handler.
    /// </summary>
    [RequiresFlipperFact]
    public async Task StorageInfo_InvalidPath_ThrowsStorageError()
    {
        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => Client.StorageInfoAsync("/nonexistent_volume"));

        Assert.Equal("storage_error", ex.ErrorCode);
    }

    // -----------------------------------------------------------------------
    // storage_list
    // -----------------------------------------------------------------------

    /// <summary>
    /// Listing the root of the internal storage must return a non-null entry
    /// array (may be empty if the volume is empty, but must not throw).
    /// Validates: <c>storage_list</c> happy-path.
    /// </summary>
    [RequiresFlipperFact]
    public async Task StorageList_InternalStorage_ReturnsEntries()
    {
        var response = await Client.StorageListAsync("/int");

        // Entries may be empty but the array itself must be non-null.
        Assert.NotNull(response.Entries);
    }

    /// <summary>
    /// Listing a non-existent directory must throw a
    /// <see cref="FlipperRpcException"/> with the <c>open_failed</c> error
    /// code.
    /// Validates: error path in the <c>storage_list</c> handler.
    /// </summary>
    [RequiresFlipperFact]
    public async Task StorageList_NonExistentPath_ThrowsOpenFailed()
    {
        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => Client.StorageListAsync("/ext/__does_not_exist__"));

        Assert.Equal("open_failed", ex.ErrorCode);
    }

    // -----------------------------------------------------------------------
    // storage_mkdir / storage_stat / storage_remove
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creating a directory, confirming it via stat, and removing it must all
    /// succeed without throwing.
    /// Validates: <c>storage_mkdir</c>, <c>storage_stat</c>, and
    /// <c>storage_remove</c> happy-paths in sequence.
    /// </summary>
    [RequiresFlipperFact]
    public async Task StorageMkdir_StatThenRemove_RoundTrip()
    {
        // Ensure the directory does not already exist.
        try { await Client.StorageRemoveAsync(ScratchDir); } catch { /* ignore */ }

        await Client.StorageMkdirAsync(ScratchDir);

        var stat = await Client.StorageStatAsync(ScratchDir);
        Assert.True(stat.IsDir, "stat.IsDir must be true for a directory");

        await Client.StorageRemoveAsync(ScratchDir);
    }

    /// <summary>
    /// Attempting to create a directory that already exists must throw a
    /// <see cref="FlipperRpcException"/> with the <c>mkdir_failed</c> error
    /// code.
    /// Validates: duplicate-mkdir error path in the daemon.
    /// </summary>
    [RequiresFlipperFact]
    public async Task StorageMkdir_AlreadyExists_ThrowsMkdirFailed()
    {
        // Create the directory, then try again.
        try { await Client.StorageRemoveAsync(ScratchDir); } catch { /* ignore */ }

        await Client.StorageMkdirAsync(ScratchDir);
        try
        {
            var ex = await Assert.ThrowsAsync<FlipperRpcException>(
                () => Client.StorageMkdirAsync(ScratchDir));

            Assert.Equal("mkdir_failed", ex.ErrorCode);
        }
        finally
        {
            await Client.StorageRemoveAsync(ScratchDir);
        }
    }

    /// <summary>
    /// Stating a non-existent path must throw a <see cref="FlipperRpcException"/>
    /// with the <c>stat_failed</c> error code.
    /// Validates: error path in the <c>storage_stat</c> handler.
    /// </summary>
    [RequiresFlipperFact]
    public async Task StorageStat_NonExistentPath_ThrowsStatFailed()
    {
        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => Client.StorageStatAsync("/ext/__no_such_file__.txt"));

        Assert.Equal("stat_failed", ex.ErrorCode);
    }

    /// <summary>
    /// Removing a non-existent path must throw a <see cref="FlipperRpcException"/>
    /// with the <c>remove_failed</c> error code.
    /// Validates: error path in the <c>storage_remove</c> handler.
    /// </summary>
    [RequiresFlipperFact]
    public async Task StorageRemove_NonExistentPath_ThrowsRemoveFailed()
    {
        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => Client.StorageRemoveAsync("/ext/__no_such_file__.txt"));

        Assert.Equal("remove_failed", ex.ErrorCode);
    }

    // -----------------------------------------------------------------------
    // storage_write / storage_read
    // -----------------------------------------------------------------------

    /// <summary>
    /// Writing a file and reading it back must produce the same content.
    /// Validates: <c>storage_write</c> and <c>storage_read</c> round-trip
    /// with raw byte data.
    /// </summary>
    [RequiresFlipperFact]
    public async Task StorageWriteThenRead_RoundTrip()
    {
        // Prepare scratch directory.
        try { await Client.StorageRemoveAsync(ScratchFile); } catch { /* ignore */ }
        try { await Client.StorageRemoveAsync(ScratchDir); } catch { /* ignore */ }

        await Client.StorageMkdirAsync(ScratchDir);

        try
        {
            const string content = "Hello, Flipper!";
            byte[] contentBytes = Encoding.UTF8.GetBytes(content);

            await Client.StorageWriteAsync(ScratchFile, contentBytes);

            var read = await Client.StorageReadAsync(ScratchFile);

            Assert.NotNull(read.Data);

            string roundTripped = Encoding.UTF8.GetString(read.Data!);
            Assert.Equal(content, roundTripped);
        }
        finally
        {
            try { await Client.StorageRemoveAsync(ScratchFile); } catch { /* ignore */ }
            try { await Client.StorageRemoveAsync(ScratchDir); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// After writing a file, <c>storage_stat</c> must report it as a non-directory
    /// with a positive size.
    /// Validates: <c>storage_write</c> + <c>storage_stat</c> integration.
    /// </summary>
    [RequiresFlipperFact]
    public async Task StorageWrite_ThenStat_ReportsFileWithPositiveSize()
    {
        try { await Client.StorageRemoveAsync(ScratchFile); } catch { /* ignore */ }
        try { await Client.StorageRemoveAsync(ScratchDir); } catch { /* ignore */ }

        await Client.StorageMkdirAsync(ScratchDir);

        try
        {
            byte[] contentBytes = Encoding.UTF8.GetBytes("stat-test");

            await Client.StorageWriteAsync(ScratchFile, contentBytes);

            var stat = await Client.StorageStatAsync(ScratchFile);

            Assert.False(stat.IsDir, "A file must not be reported as a directory");
            Assert.True(stat.Size > 0, "Written file must have positive size");
        }
        finally
        {
            try { await Client.StorageRemoveAsync(ScratchFile); } catch { /* ignore */ }
            try { await Client.StorageRemoveAsync(ScratchDir); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Reading a non-existent file must throw a <see cref="FlipperRpcException"/>
    /// with the <c>open_failed</c> error code.
    /// Validates: error path in the <c>storage_read</c> handler.
    /// </summary>
    [RequiresFlipperFact]
    public async Task StorageRead_NonExistentFile_ThrowsOpenFailed()
    {
        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => Client.StorageReadAsync("/ext/__no_such_file__.txt"));

        Assert.Equal("open_failed", ex.ErrorCode);
    }
}
