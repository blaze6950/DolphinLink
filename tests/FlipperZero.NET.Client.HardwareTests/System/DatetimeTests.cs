using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.HardwareTests.System;

/// <summary>
/// Integration tests for <see cref="FlipperRpcClient.DatetimeGetAsync"/> and
/// <see cref="FlipperRpcClient.DatetimeSetAsync"/>.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~DatetimeTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class DatetimeTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    /// <summary>
    /// <see cref="FlipperRpcClient.DatetimeGetAsync"/> must return a
    /// <see cref="DateTime"/> with plausible field values: year ≥ 2024,
    /// month 1–12, day 1–31, hour 0–23, minute 0–59, second 0–59.
    /// Validates: JSON serialisation, request-id routing, and deserialisation
    /// of the six date/time fields emitted by the daemon.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task DatetimeGet_ReturnsPlausibleDate()
    {
        var response = await Client.DatetimeGetAsync();
        var dt = response.DateTime;

        Assert.True(dt.Year >= 2024,
            $"DatetimeGetResponse.DateTime.Year must be >= 2024, got {dt.Year}");
        Assert.True(dt.Month is >= 1 and <= 12,
            $"DatetimeGetResponse.DateTime.Month must be 1–12, got {dt.Month}");
        Assert.True(dt.Day is >= 1 and <= 31,
            $"DatetimeGetResponse.DateTime.Day must be 1–31, got {dt.Day}");
        Assert.True(dt.Hour <= 23,
            $"DatetimeGetResponse.DateTime.Hour must be 0–23, got {dt.Hour}");
        Assert.True(dt.Minute <= 59,
            $"DatetimeGetResponse.DateTime.Minute must be 0–59, got {dt.Minute}");
        Assert.True(dt.Second <= 59,
            $"DatetimeGetResponse.DateTime.Second must be 0–59, got {dt.Second}");
    }

    /// <summary>
    /// Setting a known date/time and immediately reading it back must reflect
    /// the values that were written (within a ±2-second tolerance on the
    /// seconds field to account for RTC tick latency and round-trip time).
    /// Validates: <c>datetime_set</c> write path and end-to-end RTC round-trip.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task DatetimeSet_ThenGet_ReflectsNewValue()
    {
        // Save the original time so we can restore it in the finally block.
        var original = await Client.DatetimeGetAsync();

        try
        {
            var target = new DateTime(2025, 6, 15, 12, 30, 0, DateTimeKind.Unspecified);

            await Client.DatetimeSetAsync(target);

            var readback = (await Client.DatetimeGetAsync()).DateTime;

            Assert.Equal(target.Year, readback.Year);
            Assert.Equal(target.Month, readback.Month);
            Assert.Equal(target.Day, readback.Day);
            Assert.Equal(target.Hour, readback.Hour);
            Assert.Equal(target.Minute, readback.Minute);
            // Allow ±2 seconds for RTC tick and round-trip latency.
            Assert.True(readback.Second <= target.Second + 2,
                $"DateTime.Second expected ~{target.Second}, got {readback.Second}");
        }
        finally
        {
            // Always restore the original clock, even if assertions fail.
            await Client.DatetimeSetAsync(original.DateTime);
        }
    }

    /// <summary>
    /// After setting a different year and immediately restoring it, a
    /// subsequent get must return a year that matches the restored value,
    /// confirming the RTC is not permanently corrupted by this class's tests.
    /// Validates: restore path of <c>datetime_set</c>.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task DatetimeSet_ThenRestored_YearMatchesOriginal()
    {
        var before = (await Client.DatetimeGetAsync()).DateTime;

        // Set a clearly different year, then restore immediately.
        await Client.DatetimeSetAsync(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified));
        await Client.DatetimeSetAsync(before);

        var after = (await Client.DatetimeGetAsync()).DateTime;

        Assert.Equal(before.Year, after.Year);
    }
}
