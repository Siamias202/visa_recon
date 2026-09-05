using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;

namespace VISA_RECON.API.Application.Mappings.GLMappingsHelper;

/// <summary>
/// Parses GL dates as day-first. Month-first parsing is retained only as a
/// fallback for values that cannot be interpreted as day-first dates.
/// </summary>
public sealed class GlDateConverter : DateTimeConverter
{
    private static readonly string[] DayFirstFormats =
    [
        "d/M/yyyy",
        "d/M/yy",
        "d-M-yyyy",
        "d-M-yy",
        "yyyy-MM-dd",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.fff",
        "yyyy-MM-dd HH:mm:ss.ffffff"
    ];

    private static readonly string[] MonthFirstFallbackFormats =
    [
        "M/d/yyyy",
        "M/d/yy"
    ];

    public override object? ConvertFromString(
        string? text,
        IReaderRow row,
        MemberMapData memberMapData)
    {
        if (TryParse(text, out var result))
        {
            return result;
        }

        return base.ConvertFromString(text, row, memberMapData);
    }

    internal static bool TryParse(string? text, out DateTime result)
    {
        var value = text?.Trim();

        if (DateTime.TryParseExact(
                value,
                DayFirstFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out result))
        {
            return true;
        }

        return DateTime.TryParseExact(
            value,
            MonthFirstFallbackFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out result);
    }
}
