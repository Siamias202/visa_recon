using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;

namespace VISA_RECON.API.Application.Mappings.GLMappingsHelper;

/// <summary>
/// Keeps identifier fields as strings while expanding scientific notation.
/// </summary>
public sealed class GlIdentifierConverter : StringConverter
{
    public override object? ConvertFromString(
        string? text,
        IReaderRow row,
        MemberMapData memberMapData) => Normalize(text);

    internal static string Normalize(string? text)
    {
        var value = text?.Trim() ?? string.Empty;

        if (!value.Contains('E', StringComparison.OrdinalIgnoreCase) ||
            !decimal.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var number) ||
            number != decimal.Truncate(number))
        {
            return value;
        }

        return number.ToString("0", CultureInfo.InvariantCulture);
    }
}
