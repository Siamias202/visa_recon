using System.Globalization;
using ClosedXML.Excel;
using CsvHelper;
using VISA_RECON.API.Application.DTOs.GLTransaction;
using VISA_RECON.API.Application.Mappings.GLMappingsHelper;

namespace VISA_RECON.API.Application.Helper;

public static class GLUploadHelper
{
    private static readonly HashSet<string> ExpectedHeaders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "ACCOUNT NO",
            "POSTING DATE",
            "VALUE DATE",
            "BATCH ID",
            "POSTING BRANCH",
            "UNIQUEREFERENCENO",
            "DEBIT/CREDIT",
            "AMOUNT",
            "TRANSACTION CODE",
            "TRANSACTION NAME",
            "CURRENCY",
            "TIME STAMP",
            "UNIQUE ID",
            "NARRATIVE 1",
            "NARRATIVE 2",
            "RRN",
            "AUTH CODE",
            "NARRATIVE 3",
            "NARRATIVE 4"
        };

    public static async Task ReadCsvFileAsync(
        IFormFile file,
        List<UploadGLRequest> mergedRecords)
    {
        await using var stream = file.OpenReadStream();

        using var reader = new StreamReader(stream);

        using var csv = new CsvReader(
            reader,
            CultureInfo.InvariantCulture);

        if (!await csv.ReadAsync())
        {
            throw new InvalidDataException(
                $"File '{file.FileName}' has no data.");
        }

        csv.ReadHeader();

        var headers = csv.HeaderRecord;

        if (!IsValidGLTransaction(headers))
        {
            throw new InvalidDataException(
                $"Invalid header format in file '{file.FileName}'.");
        }

        csv.Context.RegisterClassMap<UploadGLRequestMappings>();

        await foreach (
            var record in csv.GetRecordsAsync<UploadGLRequest>())
        {
            ValidateRecord(record, file.FileName);

            mergedRecords.Add(record);
        }
    }

    public static async Task ReadXlsxFileAsync(
        IFormFile file,
        List<UploadGLRequest> mergedRecords)
    {
        await using var stream = file.OpenReadStream();

        using var workbook = new XLWorkbook(stream);

        var worksheet = workbook.Worksheets.FirstOrDefault();

        if (worksheet == null)
        {
            throw new InvalidDataException(
                $"File '{file.FileName}' has no worksheet.");
        }

        var firstRow = worksheet.FirstRowUsed();

        if (firstRow == null)
        {
            throw new InvalidDataException(
                $"File '{file.FileName}' has no data.");
        }

        var headerRowNumber = firstRow.RowNumber();

        var lastColumn =
            worksheet
                .Row(headerRowNumber)
                .LastCellUsed()
                ?.Address.ColumnNumber ?? 0;

        if (lastColumn == 0)
        {
            throw new InvalidDataException(
                $"File '{file.FileName}' has no headers.");
        }

        // ---------------------------------------------
        // Read headers
        // ---------------------------------------------

        var headers = Enumerable
            .Range(1, lastColumn)
            .Select(column =>
                worksheet
                    .Cell(headerRowNumber, column)
                    .GetString()
                    .Trim())
            .ToArray();

        if (!IsValidGLTransaction(headers))
        {
            throw new InvalidDataException(
                $"Invalid header format in file '{file.FileName}'.");
        }

        // ---------------------------------------------
        // Map header -> column number
        // ---------------------------------------------

        var columnMap = headers
            .Select((header, index) =>
                new
                {
                    Header = NormalizeHeader(header),
                    Column = index + 1
                })
            .ToDictionary(
                x => x.Header,
                x => x.Column,
                StringComparer.OrdinalIgnoreCase);

        var lastRowNumber =
            worksheet.LastRowUsed()?.RowNumber()
            ?? headerRowNumber;

        if (lastRowNumber <= headerRowNumber)
        {
            return;
        }

        // ---------------------------------------------
        // Read rows directly
        // ---------------------------------------------

        for (
            var rowNumber = headerRowNumber + 1;
            rowNumber <= lastRowNumber;
            rowNumber++)
        {
            var row = worksheet.Row(rowNumber);

            if (row.IsEmpty())
            {
                continue;
            }

            var record = new UploadGLRequest
            {
                AccountNo = GetIdentifier(
                    worksheet.Cell(
                        rowNumber,
                        columnMap["ACCOUNT NO"])),

                PostingDate = GetDateTime(
                    worksheet.Cell(
                        rowNumber,
                        columnMap["POSTING DATE"]),
                    "POSTING DATE"),

                ValueDate = GetDateTime(
                    worksheet.Cell(
                        rowNumber,
                        columnMap["VALUE DATE"]),
                    "VALUE DATE"),

                BatchId = GetString(
                    worksheet.Cell(
                        rowNumber,
                        columnMap["BATCH ID"])),

                PostingBranch = GetString(
                    worksheet.Cell(
                        rowNumber,
                        columnMap["POSTING BRANCH"])),

                UniqueReferenceNo = GetIdentifier(
                    worksheet.Cell(
                        rowNumber,
                        columnMap["UNIQUEREFERENCENO"])),

                DebitCredit = GetString(
                    worksheet.Cell(
                        rowNumber,
                        columnMap["DEBIT/CREDIT"])),

                Amount = GetDecimal(
                    worksheet.Cell(
                        rowNumber,
                        columnMap["AMOUNT"]),
                    "AMOUNT"),

                TransactionCode = GetString(
                    worksheet.Cell(
                        rowNumber,
                        columnMap["TRANSACTION CODE"])),

                TransactionName = GetString(
                    worksheet.Cell(
                        rowNumber,
                        columnMap["TRANSACTION NAME"])),

                Currency = GetString(
                    worksheet.Cell(
                        rowNumber,
                        columnMap["CURRENCY"])),

                TimeStamp = GetDateTime(
                    worksheet.Cell(
                        rowNumber,
                        columnMap["TIME STAMP"]),
                    "TIME STAMP"),

                UniqueId = GetIdentifier(
                    worksheet.Cell(
                        rowNumber,
                        columnMap["UNIQUE ID"])),

                Narrative1 = GetString(
                    worksheet.Cell(
                        rowNumber,
                        columnMap["NARRATIVE 1"])),

                Narrative2 = GetString(
                    worksheet.Cell(
                        rowNumber,
                        columnMap["NARRATIVE 2"])),

                RRN = GetIdentifier(
                    worksheet.Cell(
                        rowNumber,
                        columnMap["RRN"])),

                AuthCode = GetString(
                    worksheet.Cell(
                        rowNumber,
                        columnMap["AUTH CODE"])),

                Narrative3 = GetString(
                    worksheet.Cell(
                        rowNumber,
                        columnMap["NARRATIVE 3"])),

                Narrative4 = GetString(
                    worksheet.Cell(
                        rowNumber,
                        columnMap["NARRATIVE 4"]))
            };

            ValidateRecord(
                record,
                file.FileName);

            mergedRecords.Add(record);
        }

        await Task.CompletedTask;
    }

    // ============================================================
    // Excel value readers
    // ============================================================

    private static string GetString(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return string.Empty;
        }

        return cell.GetString().Trim();
    }

    private static string GetIdentifier(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return string.Empty;
        }

        var value = cell.DataType == XLDataType.Number
            ? cell.GetDouble().ToString("0", CultureInfo.InvariantCulture)
            : cell.GetString();

        return GlIdentifierConverter.Normalize(value);
    }

    private static string GetAccountNumber(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return string.Empty;
        }

        /*
         * IMPORTANT:
         *
         * Account number is an identifier.
         * Never use GetDouble() here.
         *
         * Example:
         *
         * 12605203893090000000
         *
         * must NOT become:
         *
         * 1.26052038909E+19
         */

        if (cell.DataType == XLDataType.Text)
        {
            return cell.GetString().Trim();
        }

        /*
         * If Excel has stored the account number as a numeric
         * value, use the displayed/formatted value.
         */
        return cell.GetFormattedString().Trim();
    }

    private static DateOnly GetDateOnly(
        IXLCell cell,
        string fieldName)
    {
        if (cell.IsEmpty())
        {
            return default;
        }

        try
        {
            if (cell.DataType == XLDataType.DateTime)
            {
                return DateOnly.FromDateTime(
                    cell.GetDateTime());
            }

            if (cell.DataType == XLDataType.Number)
            {
                var oaDate = cell.GetDouble();

                return DateOnly.FromDateTime(
                    DateTime.FromOADate(oaDate));
            }

            var text = cell
                .GetString()
                .Trim();

            if (DateOnly.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
            {
                return date;
            }

            if (DateTime.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dateTime))
            {
                return DateOnly.FromDateTime(dateTime);
            }

            throw new InvalidDataException(
                $"Invalid {fieldName} value '{text}'.");
        }
        catch (Exception ex)
            when (ex is FormatException ||
                  ex is ArgumentException)
        {
            throw new InvalidDataException(
                $"Invalid {fieldName} value in Excel.",
                ex);
        }
    }


    private static bool TryParseExcelTotalHours(
    string value,
    out DateTime result)
    {
        result = default;

        var parts = value.Split(':');

        if (parts.Length != 3)
        {
            return false;
        }

        // Example:
        // 1106722:55:06.184
        //
        // parts[0] = 1106722
        // parts[1] = 55
        // parts[2] = 06.184

        if (!long.TryParse(
            parts[0],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var totalHours))
        {
            return false;
        }

        if (!int.TryParse(
            parts[1],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var minutes))
        {
            return false;
        }

        if (!decimal.TryParse(
            parts[2],
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var seconds))
        {
            return false;
        }

        if (minutes < 0 || minutes > 59)
        {
            return false;
        }

        if (seconds < 0 || seconds >= 60)
        {
            return false;
        }

        try
        {
            var totalMilliseconds =
                (decimal)totalHours * 60m * 60m * 1000m
                + minutes * 60m * 1000m
                + seconds * 1000m;

            var timeSpan = TimeSpan.FromMilliseconds(
                (double)totalMilliseconds);

            // Excel's DateTime epoch
            var excelEpoch =
                new DateTime(1899, 12, 30);

            result = excelEpoch.Add(timeSpan);

            return true;
        }
        catch
        {
            return false;
        }
    }


    private static DateTime GetDateTime(
    IXLCell cell,
    string fieldName)
    {
        if (cell.IsEmpty())
        {
            return default;
        }

        // Normal Excel DateTime
        if (cell.DataType == XLDataType.DateTime)
        {
            return cell.GetDateTime();
        }

        // Excel numeric date serial
        if (cell.DataType == XLDataType.Number)
        {
            return DateTime.FromOADate(
                cell.GetDouble());
        }

        var text = cell.GetString().Trim();

        // ---------------------------------------------------------
        // Normal timestamp formats
        // ---------------------------------------------------------

        var formats = new[]
        {
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.fff",
        "yyyy-MM-dd HH:mm:ss.ffffff",

        "M/d/yyyy h:mm:ss tt",
        "M/d/yyyy  h:mm:ss tt",

        "MM/dd/yyyy h:mm:ss tt",
        "MM/dd/yyyy  h:mm:ss tt"
    };

        if (DateTime.TryParseExact(
            text,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var result))
        {
            return result;
        }

        if (DateTime.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out result))
        {
            return result;
        }

        // ---------------------------------------------------------
        // Excel [h]:mm:ss.fff format
        //
        // Example:
        // 1106722:55:06.184
        //
        // This represents the Excel DateTime:
        // 2026-03-30 18:06:28.184
        // ---------------------------------------------------------

        if (TryParseExcelTotalHours(
            text,
            out result))
        {
            return result;
        }

        throw new InvalidDataException(
            $"Invalid {fieldName} value '{text}'.");
    }


    private static decimal GetDecimal(
        IXLCell cell,
        string fieldName)
    {
        if (cell.IsEmpty())
        {
            return 0m;
        }

        if (cell.DataType == XLDataType.Number)
        {
            /*
             * Don't use GetDouble() for money.
             *
             * Convert directly to decimal.
             */
            var text = cell
                .GetFormattedString()
                .Trim();

            if (decimal.TryParse(
                text,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var result))
            {
                return result;
            }

            /*
             * Fallback for cells whose formatting is unusual.
             */
            return Convert.ToDecimal(
                cell.Value,
                CultureInfo.InvariantCulture);
        }

        var stringValue = cell
            .GetString()
            .Trim();

        if (decimal.TryParse(
            stringValue,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var decimalResult))
        {
            return decimalResult;
        }

        throw new InvalidDataException(
            $"Invalid {fieldName} value '{stringValue}'.");
    }

    // ============================================================
    // Validation
    // ============================================================

    private static void ValidateRecord(
        UploadGLRequest record,
        string fileName)
    {
        if (string.IsNullOrWhiteSpace(record.AccountNo))
        {
            throw new InvalidDataException(
                $"File '{fileName}' contains an empty ACCOUNT NO.");
        }

        if (record.AccountNo.Length > 20)
        {
            throw new InvalidDataException(
                $"ACCOUNT NO '{record.AccountNo}' exceeds 20 characters.");
        }

        if (string.IsNullOrWhiteSpace(record.Currency))
        {
            throw new InvalidDataException(
                $"File '{fileName}' contains an empty CURRENCY.");
        }

        if (record.Currency.Length != 3)
        {
            throw new InvalidDataException(
                $"Invalid CURRENCY '{record.Currency}'.");
        }

        if (record.DebitCredit != "C" &&
            record.DebitCredit != "D")
        {
            throw new InvalidDataException(
                $"Invalid DEBIT/CREDIT value '{record.DebitCredit}'. " +
                "Expected C or D.");
        }
    }

    // ============================================================
    // Header validation
    // ============================================================

    private static string NormalizeHeader(
        string header)
    {
        return header.Trim();
    }

    private static bool IsValidGLTransaction(
        string[]? headers)
    {
        if (headers == null || headers.Length == 0)
        {
            return false;
        }

        var normalizedHeaders = headers
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(NormalizeHeader)
            .ToHashSet(
                StringComparer.OrdinalIgnoreCase);

        return ExpectedHeaders.SetEquals(
            normalizedHeaders);
    }
}
