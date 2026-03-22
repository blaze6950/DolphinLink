using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.System;

/// <summary>
/// Returns the current RTC date and time from the Flipper.
///
/// Wire format (request):
/// <code>{"i":N,"c":7}</code>
///
/// Wire format (response):
/// <code>{"t":0,"i":N,"p":{"yr":2025,"mo":6,"dy":1,"hr":12,"mn":0,"sc":0,"wd":7}}</code>
///
/// <c>wd</c> follows the Flipper convention: 1 = Monday … 7 = Sunday.
/// The <c>wd</c> field is derived from the <see cref="DateTime.DayOfWeek"/>
/// property and does not need to be decoded separately.
/// </summary>
public readonly partial struct DatetimeGetCommand : IRpcCommand<DatetimeGetResponse>
{
    // CommandName and CommandId come from Generated/Commands/System/DatetimeGetCommand.g.cs
    // WriteArgs: no arguments needed
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
/// Deserialises the seven individual JSON fields (<c>yr</c>, <c>mo</c>, <c>dy</c>,
/// <c>hr</c>, <c>mn</c>, <c>sc</c>, <c>wd</c>) emitted by the daemon
/// into a single <see cref="DateTime"/>.
/// The <c>wd</c> field is ignored on read — it is fully redundant with
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
                case "yr": year   = reader.GetInt32(); break;
                case "mo": month  = reader.GetInt32(); break;
                case "dy": day    = reader.GetInt32(); break;
                case "hr": hour   = reader.GetInt32(); break;
                case "mn": minute = reader.GetInt32(); break;
                case "sc": second = reader.GetInt32(); break;
                // "wd" and any other fields are intentionally skipped
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
        writer.WriteNumber("yr", dt.Year);
        writer.WriteNumber("mo", dt.Month);
        writer.WriteNumber("dy", dt.Day);
        writer.WriteNumber("hr", dt.Hour);
        writer.WriteNumber("mn", dt.Minute);
        writer.WriteNumber("sc", dt.Second);
        // weekday: Flipper uses ISO 8601 (1=Mon…7=Sun); .NET DayOfWeek is 0=Sun…6=Sat
        int weekday = dt.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)dt.DayOfWeek;
        writer.WriteNumber("wd", weekday);
        writer.WriteEndObject();
    }
}
