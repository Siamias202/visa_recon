using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using VISA_RECON.API.Application.DTOs.AcquiringTransaction;
using VISA_RECON.API.Application.Mappings.GLMappingsHelper;

namespace VISA_RECON.API.Application.Helper;

public static class AcquiringUploadHelper
{
    public static async Task<List<AcquiringFeTransaction>> ReadFeAsync(IFormFile file)
    {
        var rows = await ReadRowsAsync(file);
        return rows.Where(r => Get(r, "ISSUERINST") == "9006")
            .Select(r => new AcquiringFeTransaction
            {
                AtmId = Get(r, "ATMID"),
                Reversal = ToBool(Get(r, "REVERSAL")),
                RequestAmount = ToDecimal(Get(r, "REQUESTAMOUNT")),
                Bills1 = ToInt(Get(r, "BILLS1")),
                Bills2 = ToInt(Get(r, "BILLS2")),
                Bills3 = ToInt(Get(r, "BILLS3")),
                Bills4 = ToInt(Get(r, "BILLS4")),
                Udate = ToInt(Get(r, "UDATE")),
                Time = Get(r, "TIME"),
                UtrNo = GlIdentifierConverter.Normalize(Get(r, "UTRNO")),
                IssuerInst = Get(r, "ISSUERINST"),
                ReferenceNum = GlIdentifierConverter.Normalize(Get(r, "REFERENCENUM")),
                AuthCode = Get(r, "AUTHCODE"),
                Acct1 = Get(r, "ACCT1"),
                HpanCard = Get(r, "HPANCARD")
            })
            .ToList();
    }

    public static async Task<List<AcquiringEpTransaction>> ReadEpAsync(IFormFile file)
    {
        var rows = await ReadRowsAsync(file);
        return rows.Select(r => new AcquiringEpTransaction
        {
            Pan = GlIdentifierConverter.Normalize(Get(r, "PAN")),
            Rrn = GlIdentifierConverter.Normalize(Get(r, "RRN")),
            Acq = Get(r, "ACQ"),
            Integratedp = Get(r, "INTEGRATEDP"),
            Aymen = Get(r, "AYMEN"),
            Tsyste = Get(r, "TSYSTE"),
            M = Get(r, "M"),
            AmountBdt = ToDecimal(Get(r, "AMOUNTBDT")),
            Currency = Get(r, "CURRENCY"),
            AmountUsd = ToDecimal(Get(r, "AMOUNTUSD"))
        }).ToList();
    }

    private static async Task<List<Dictionary<string, string>>> ReadRowsAsync(IFormFile file)
    {
        if (file.Length == 0)
            throw new InvalidDataException($"File '{file.FileName}' is empty.");

        return Path.GetExtension(file.FileName).ToLowerInvariant() switch
        {
            ".csv" => await ReadCsvAsync(file),
            ".xlsx" => ReadXlsx(file),
            _ => throw new InvalidDataException(
                $"Unsupported file '{file.FileName}'. Only CSV and XLSX are supported.")
        };
    }

    private static async Task<List<Dictionary<string, string>>> ReadCsvAsync(IFormFile file)
    {
        await using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            BadDataFound = null,
            MissingFieldFound = null,
            HeaderValidated = null,
            TrimOptions = TrimOptions.Trim
        });

        if (!await csv.ReadAsync() || !csv.ReadHeader())
            throw new InvalidDataException($"File '{file.FileName}' has no header row.");

        var headers = csv.HeaderRecord ?? [];
        var result = new List<Dictionary<string, string>>();
        while (await csv.ReadAsync())
        {
            var row = headers.ToDictionary(
                Normalize,
                h => csv.GetField(h)?.Trim() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
            if (row.Values.Any(v => !string.IsNullOrWhiteSpace(v))) result.Add(row);
        }
        return result;
    }

    private static List<Dictionary<string, string>> ReadXlsx(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        var range = sheet.RangeUsed()
            ?? throw new InvalidDataException($"File '{file.FileName}' has no data.");
        var headers = range.FirstRow().Cells(1, range.ColumnCount())
            .Select(c => Normalize(c.GetString())).ToArray();
        var result = new List<Dictionary<string, string>>();
        foreach (var excelRow in range.RowsUsed().Skip(1))
        {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Length; i++)
                row[headers[i]] = excelRow.Cell(i + 1).GetFormattedString().Trim();
            if (row.Values.Any(v => !string.IsNullOrWhiteSpace(v))) result.Add(row);
        }
        return result;
    }

    private static string Get(IReadOnlyDictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var value) ? value : string.Empty;

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static int ToInt(string value) => string.IsNullOrWhiteSpace(value) ? 0 :
        int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : throw new InvalidDataException($"'{value}' is not a valid integer.");

    private static decimal ToDecimal(string value) => string.IsNullOrWhiteSpace(value) ? 0 :
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : throw new InvalidDataException($"'{value}' is not a valid amount.");

    private static bool ToBool(string value) =>
        value.Trim().ToUpperInvariant() is "1" or "TRUE" or "YES" or "Y";
}
