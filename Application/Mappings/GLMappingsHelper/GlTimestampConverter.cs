using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;

namespace VISA_RECON.API.Application.Mappings.GLMappingsHelper;

/// <summary>
/// Converts the GL timestamp column. Values such as "26:29.0" are exported
/// as minutes:seconds.fraction and therefore represent 12:26:29 AM.
/// </summary>
public sealed class GlTimestampConverter : DateTimeConverter
{
    private static readonly string[] FullTimestampFormats =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.fff",
        "yyyy-MM-dd HH:mm:ss.ffffff"
    ];

    public override object? ConvertFromString(
        string? text,
        IReaderRow row,
        MemberMapData memberMapData)
    {
        var value = text?.Trim();

        if (string.IsNullOrWhiteSpace(value))
        {
            return base.ConvertFromString(text, row, memberMapData);
        }

        if (DateTime.TryParseExact(
                value,
                FullTimestampFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var fullTimestamp))
        {
            return fullTimestamp;
        }

        if (TryParseMinuteSecond(value, out var timeOfDay))
        {
            var postingDateText = row.GetField("POSTING DATE");
            if (!GlDateConverter.TryParse(postingDateText, out var postingDate))
            {
                return base.ConvertFromString(text, row, memberMapData);
            }

            return postingDate.Date.Add(timeOfDay);
        }

        return base.ConvertFromString(text, row, memberMapData);
    }

    private static bool TryParseMinuteSecond(
        string value,
        out TimeSpan result)
    {
        result = default;

        var parts = value.Split(':');
        if (parts.Length != 2 ||
            !int.TryParse(
                parts[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var minutes) ||
            !decimal.TryParse(
                parts[1],
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var seconds) ||
            minutes is < 0 or > 59 ||
            seconds is < 0 or >= 60)
        {
            return false;
        }

        result = TimeSpan.FromMinutes(minutes)
            + TimeSpan.FromSeconds((double)seconds);
        return true;
    }
}
