using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.System;

/// <summary>
/// Sets the RTC date and time on the Flipper.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"datetime_set","year":2025,"month":6,"day":1,"hour":12,"minute":0,"second":0,"weekday":7}</code>
///
/// Wire format (response):
/// <code>{"id":N,"status":"ok"}</code>
///
/// <c>weekday</c> follows the Flipper convention: 1 = Monday … 7 = Sunday.
/// It is derived automatically from <see cref="DateTime.DayOfWeek"/>.
/// </summary>
public readonly struct DatetimeSetCommand : IRpcCommand<DatetimeSetResponse>
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
    public void WriteArgs(Utf8JsonWriter writer)
    {
        var dt = DateTime;
        writer.WriteNumber("year",   dt.Year);
        writer.WriteNumber("month",  dt.Month);
        writer.WriteNumber("day",    dt.Day);
        writer.WriteNumber("hour",   dt.Hour);
        writer.WriteNumber("minute", dt.Minute);
        writer.WriteNumber("second", dt.Second);
        // Flipper uses ISO 8601 weekday: 1 = Monday … 7 = Sunday.
        // .NET DayOfWeek: 0 = Sunday … 6 = Saturday.
        int weekday = dt.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)dt.DayOfWeek;
        writer.WriteNumber("weekday", weekday);
    }
}

/// <summary>Response to <see cref="DatetimeSetCommand"/>.</summary>
public readonly struct DatetimeSetResponse : IRpcCommandResponse { }
