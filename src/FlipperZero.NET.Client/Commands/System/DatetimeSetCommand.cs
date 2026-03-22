using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.System;

/// <summary>
/// Sets the RTC date and time on the Flipper.
///
/// Wire format (request):
/// <code>{"i":N,"c":8,"yr":2025,"mo":6,"dy":1,"hr":12,"mn":0,"sc":0,"wd":7}</code>
///
/// Wire format (response):
/// <code>{"t":0,"i":N}</code>
///
/// <c>wd</c> follows the Flipper convention: 1 = Monday … 7 = Sunday.
/// It is derived automatically from <see cref="DateTime.DayOfWeek"/>.
/// </summary>
public readonly partial struct DatetimeSetCommand : IRpcCommand<DatetimeSetResponse>
{
    /// <param name="dateTime">
    /// The date and time to set on the Flipper RTC.
    /// <see cref="DateTime.Kind"/> is ignored — the Flipper RTC has no timezone concept.
    /// </param>
    public DatetimeSetCommand(DateTime dateTime) => DateTime = dateTime;

    /// <summary>The date and time to set.</summary>
    public DateTime DateTime { get; }

    /// <inheritdoc />
    public string CommandName => "datetime_set";

    /// <inheritdoc />
    public int CommandId => 8;

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        var dt = DateTime;
        writer.WriteNumber("yr", dt.Year);
        writer.WriteNumber("mo", dt.Month);
        writer.WriteNumber("dy", dt.Day);
        writer.WriteNumber("hr", dt.Hour);
        writer.WriteNumber("mn", dt.Minute);
        writer.WriteNumber("sc", dt.Second);
        // Flipper uses ISO 8601 weekday: 1 = Monday … 7 = Sunday.
        // .NET DayOfWeek: 0 = Sunday … 6 = Saturday.
        int weekday = dt.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)dt.DayOfWeek;
        writer.WriteNumber("wd", weekday);
    }
}
