using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.System;

/// <summary>
/// Returns the current RTC date and time from the Flipper.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"datetime_get"}</code>
///
/// Wire format (response):
/// <code>{"t":0,"i":N,"p":{"year":2025,"month":6,"day":1,"hour":12,"minute":0,"second":0,"weekday":7}}</code>
///
/// <c>weekday</c> follows the Flipper convention: 1 = Monday … 7 = Sunday.
/// The <c>weekday</c> field is derived from the <see cref="DateTime.DayOfWeek"/>
/// property and does not need to be decoded separately.
/// </summary>
public readonly struct DatetimeGetCommand : IRpcCommand<DatetimeGetResponse>
{
    /// <inheritdoc />
    public string CommandName => "datetime_get";

    /// <summary>No arguments.</summary>
    public void WriteArgs(Utf8JsonWriter writer) { }
}

/// <summary>Response to <see cref="DatetimeGetCommand"/>.</summary>
[JsonConverter(typeof(DatetimeGetResponseJsonConverter))]
public readonly struct DatetimeGetResponse : IRpcCommandResponse
{
    /// <summary>
    /// The RTC date and time as reported by the Flipper.
    /// <see cref="DateTime.Kind"/> is always <see cref="DateTimeKind.Unspecified"/>
    /// because the Flipper RTC has no timezone concept.
    /// </summary>
    public DateTime DateTime { get; init; }
}

/// <summary>
/// Deserialises the seven individual JSON fields (<c>year</c>, <c>month</c>, <c>day</c>,
/// <c>hour</c>, <c>minute</c>, <c>second</c>, <c>weekday</c>) emitted by the daemon
/// into a single <see cref="DateTime"/>.
/// The <c>weekday</c> field is ignored on read — it is fully redundant with
/// <see cref="DateTime.DayOfWeek"/>.
/// </summary>
internal sealed class DatetimeGetResponseJsonConverter : JsonConverter<DatetimeGetResponse>
{
    public override DatetimeGetResponse Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        int year = 1, month = 1, day = 1, hour = 0, minute = 0, second = 0;

        reader.Read(); // StartObject
        while (reader.TokenType != JsonTokenType.EndObject)
        {
            var propName = reader.GetString();
            reader.Read(); // value

            switch (propName)
            {
                case "year": year = reader.GetInt32(); break;
                case "month": month = reader.GetInt32(); break;
                case "day": day = reader.GetInt32(); break;
                case "hour": hour = reader.GetInt32(); break;
                case "minute": minute = reader.GetInt32(); break;
                case "second": second = reader.GetInt32(); break;
                // "weekday" and any other fields are intentionally skipped
            }
            reader.Read(); // next property name or EndObject
        }

        return new DatetimeGetResponse
        {
            DateTime = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified),
        };
    }

    public override void Write(Utf8JsonWriter writer, DatetimeGetResponse value, JsonSerializerOptions options)
    {
        var dt = value.DateTime;
        writer.WriteStartObject();
        writer.WriteNumber("year", dt.Year);
        writer.WriteNumber("month", dt.Month);
        writer.WriteNumber("day", dt.Day);
        writer.WriteNumber("hour", dt.Hour);
        writer.WriteNumber("minute", dt.Minute);
        writer.WriteNumber("second", dt.Second);
        // weekday: Flipper uses ISO 8601 (1=Mon…7=Sun); .NET DayOfWeek is 0=Sun…6=Sat
        int weekday = dt.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)dt.DayOfWeek;
        writer.WriteNumber("weekday", weekday);
        writer.WriteEndObject();
    }
}
