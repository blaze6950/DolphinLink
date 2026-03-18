namespace FlipperZero.NET.Client.IntegrationTests;

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
    /// <see cref="FlipperRpcClient.DatetimeGetAsync"/> must return a date with
    /// plausible field values: year ≥ 2024, month 1–12, day 1–31, hour 0–23,
    /// minute 0–59, second 0–59.
    /// Note: the daemon does not include <c>weekday</c> in the response JSON so
    /// <see cref="Commands.DatetimeGetResponse.Weekday"/> will always be 0.
    /// Validates: JSON serialisation, request-id routing, and deserialisation
    /// of the six date/time fields emitted by the daemon.
    /// </summary>
    [RequiresFlipperFact]
    public async Task DatetimeGet_ReturnsPlausibleDate()
    {
        var dt = await Client.DatetimeGetAsync();

        Assert.True(dt.Year >= 2024,
            $"DatetimeGetResponse.Year must be >= 2024, got {dt.Year}");
        Assert.True(dt.Month is >= 1 and <= 12,
            $"DatetimeGetResponse.Month must be 1–12, got {dt.Month}");
        Assert.True(dt.Day is >= 1 and <= 31,
            $"DatetimeGetResponse.Day must be 1–31, got {dt.Day}");
        Assert.True(dt.Hour <= 23,
            $"DatetimeGetResponse.Hour must be 0–23, got {dt.Hour}");
        Assert.True(dt.Minute <= 59,
            $"DatetimeGetResponse.Minute must be 0–59, got {dt.Minute}");
        Assert.True(dt.Second <= 59,
            $"DatetimeGetResponse.Second must be 0–59, got {dt.Second}");
    }

    /// <summary>
    /// Setting a known date/time and immediately reading it back must reflect
    /// the values that were written (within a ±2-second tolerance on the
    /// seconds field to account for RTC tick latency and round-trip time).
    /// Note: the daemon ignores the <c>weekday</c> argument and does not
    /// include it in the response, so it is not asserted here.
    /// Validates: <c>datetime_set</c> write path and end-to-end RTC round-trip.
    /// </summary>
    [RequiresFlipperFact]
    public async Task DatetimeSet_ThenGet_ReflectsNewValue()
    {
        // Save the original time so we can restore it in the finally block.
        var original = await Client.DatetimeGetAsync();

        try
        {
            // Set a well-known, clearly synthetic timestamp.
            const uint year = 2025, month = 6, day = 15;
            const uint hour = 12, minute = 30, second = 0;

            await Client.DatetimeSetAsync(year, month, day, hour, minute, second, weekday: 1);

            var readback = await Client.DatetimeGetAsync();

            Assert.Equal(year, readback.Year);
            Assert.Equal(month, readback.Month);
            Assert.Equal(day, readback.Day);
            Assert.Equal(hour, readback.Hour);
            Assert.Equal(minute, readback.Minute);
            // Allow ±2 seconds for RTC tick and round-trip latency.
            Assert.True(readback.Second <= second + 2,
                $"DatetimeGetResponse.Second expected ~{second}, got {readback.Second}");
        }
        finally
        {
            // Always restore the original clock, even if assertions fail.
            await Client.DatetimeSetAsync(
                original.Year, original.Month, original.Day,
                original.Hour, original.Minute, original.Second,
                weekday: 1);
        }
    }

    /// <summary>
    /// After setting a different year and immediately restoring it, a
    /// subsequent get must return a year that matches the restored value,
    /// confirming the RTC is not permanently corrupted by this class's tests.
    /// Validates: restore path of <c>datetime_set</c>.
    /// </summary>
    [RequiresFlipperFact]
    public async Task DatetimeSet_ThenRestored_YearMatchesOriginal()
    {
        var before = await Client.DatetimeGetAsync();

        // Set a clearly different year, then restore immediately.
        await Client.DatetimeSetAsync(2000, 1, 1, 0, 0, 0, weekday: 1);
        await Client.DatetimeSetAsync(
            before.Year, before.Month, before.Day,
            before.Hour, before.Minute, before.Second,
            weekday: 1);

        var after = await Client.DatetimeGetAsync();

        Assert.Equal(before.Year, after.Year);
    }
}
