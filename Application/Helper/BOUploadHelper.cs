using System.Globalization;
using ClosedXML.Excel;
using CsvHelper;
using VISA_RECON.API.Application.DTOs.BOTransaction;
using VISA_RECON.API.Application.Mappings.BOMappingsHelper;
using VISA_RECON.API.Application.Mappings.GLMappingsHelper;

namespace VISA_RECON.API.Application.Helper;

public static class BOUploadHelper
{
    // ============================================================
    // Expected BO Headers
    //
    // IMPORTANT:
    //
    // TRX_TYPE  = Column 5
    // TXN_TYPE  = Column 40
    //
    // They are two DIFFERENT fields.
    //
    // Required columns = 54 (additional columns are allowed)
    // ============================================================

    private static readonly string[] ExpectedHeaderOrder =
    {
        "SESSION_ID",               // 1
        "BO_OPER_ID",               // 2
        "EP_STTL_DATE",             // 3
        "RUN_DATE",                 // 4
        "TRX_TYPE",                 // 5
        "MESSAGE_TYPE",             // 6
        "CLR_STATUS",               // 7
        "CONTRACT_TYPE",            // 8
        "CARD_NUMBER",              // 9
        "ACCOUNT_NUMBER",           // 10
        "SENDER_ACCOUNT_NUMBER",    // 11
        "AUTH_CODE",                // 12
        "ARN",                      // 13
        "TRANS_DATE",               // 14
        "CLR_TXN_AMOUNT",           // 15
        "TXN_CURRENCY",             // 16
        "BILL_AMT",                 // 17
        "ACCT_CURR",                // 18
        "STTL_AMOUNT",              // 19
        "ST_REV",                   // 20
        "MATCH_STATUS",             // 21
        "AUTH_ID",                  // 22
        "MCC",                      // 23
        "MERCHANT_NUMBER",          // 24
        "TERMINAL_NUMBER",          // 25
        "MERCHANT_NAME",            // 26
        "MERCHANT_CITY",            // 27
        "MERCHANT_COUNTRY",         // 28
        "AUTH_OPR_ID",              // 29
        "BASE_II_ID",               // 30
        "TRANSACTION_DATE",         // 31
        "AUTH_CARD_NUMBER",         // 32
        "REVERSAL_FLAG",            // 33
        "TXN_AMOUNT",               // 34
        "AUTH_CURRENCY",            // 35
        "BILLING_AMOUNT",           // 36
        "FEES",                     // 37
        "BILLING_CURRENCY",         // 38
        "STATUS",                   // 39
        "TXN_TYPE",                 // 40
        "AUTH_MESSAGE_TYPE",        // 41
        "AUTH_MCC",                 // 42
        "AUTH_MID",                 // 43
        "AUTH_MERCHANT_NAME",       // 44
        "AUTH_CITY",                // 45
        "AUTH_COUNTRY",             // 46
        "AUTH_TID",                 // 47
        "AUTH_ACCT_UMBER",          // 48
        "POS_COND_CODE",            // 49
        "UTRNNO",                   // 50
        "TRACE_NUMBER",             // 51
        "TRACE_TO_CBS",             // 52
        "RRN",                      // 53
        "AUTH"                      // 54
    };

    private static readonly HashSet<string> ExpectedHeaders =
        new(
            ExpectedHeaderOrder,
            StringComparer.OrdinalIgnoreCase);


    // ============================================================
    // CSV
    // ============================================================

    public static async Task ReadCsvFileAsync(
        IFormFile file,
        List<UploadBORequest> mergedRecords)
    {
        if (file == null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        if (mergedRecords == null)
        {
            throw new ArgumentNullException(nameof(mergedRecords));
        }

        if (file.Length == 0)
        {
            throw new InvalidDataException(
                $"File '{file.FileName}' is empty.");
        }

        await using var stream = file.OpenReadStream();

        using var reader = new StreamReader(
            stream,
            detectEncodingFromByteOrderMarks: true);

        using var csv = new CsvReader(
            reader,
            CultureInfo.InvariantCulture);

        // --------------------------------------------------------
        // Read header
        // --------------------------------------------------------

        if (!await csv.ReadAsync())
        {
            throw new InvalidDataException(
                $"File '{file.FileName}' has no data.");
        }

        csv.ReadHeader();

        var headers = csv.HeaderRecord;

        if (!IsValidBOTransaction(
                headers,
                out var headerError))
        {
            throw new InvalidDataException(
                $"Invalid header format in file '{file.FileName}'. " +
                headerError);
        }

        // --------------------------------------------------------
        // Register CSV mapping
        // --------------------------------------------------------

        csv.Context.RegisterClassMap<
            UploadBORequestMappings>();

        // --------------------------------------------------------
        // Read records
        // --------------------------------------------------------

        await foreach (
            var record in csv.GetRecordsAsync<UploadBORequest>())
        {
            NormalizeMatchingIdentifiers(record);

            if (IssuingUploadCleaning.ShouldRemove(record))
            {
                continue;
            }

            ValidateRecord(
                record,
                file.FileName);

            mergedRecords.Add(record);
        }
    }


    // ============================================================
    // XLSX
    // ============================================================

    public static async Task ReadXlsxFileAsync(
        IFormFile file,
        List<UploadBORequest> mergedRecords)
    {
        if (file == null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        if (mergedRecords == null)
        {
            throw new ArgumentNullException(nameof(mergedRecords));
        }

        if (file.Length == 0)
        {
            throw new InvalidDataException(
                $"File '{file.FileName}' is empty.");
        }

        await using var stream =
            file.OpenReadStream();

        using var workbook =
            new XLWorkbook(stream);

        // ========================================================
        // Get worksheet
        // ========================================================

        var worksheet =
            workbook.Worksheets.FirstOrDefault();

        if (worksheet == null)
        {
            throw new InvalidDataException(
                $"File '{file.FileName}' has no worksheet.");
        }

        // ========================================================
        // Find first used row
        // ========================================================

        var firstRow =
            worksheet.FirstRowUsed();

        if (firstRow == null)
        {
            throw new InvalidDataException(
                $"File '{file.FileName}' has no data.");
        }

        var headerRowNumber =
            firstRow.RowNumber();

        // ========================================================
        // Find last used column in header row
        // ========================================================

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

        // ========================================================
        // Read XLSX headers
        // ========================================================

        var headers = Enumerable
            .Range(1, lastColumn)
            .Select(column =>
                NormalizeHeader(
                    worksheet
                        .Cell(
                            headerRowNumber,
                            column)
                        .GetString()))
            .ToArray();

        // ========================================================
        // Validate headers
        // ========================================================

        if (!IsValidBOTransaction(
                headers,
                out var headerError))
        {
            throw new InvalidDataException(
                $"Invalid header format in file '{file.FileName}'. " +
                headerError);
        }

        // ========================================================
        // Build the required header -> column map.
        // Extra columns in the uploaded file are intentionally
        // ignored. Prefer an exact match when an extra column only
        // differs from a required header by casing (STATUS/Status).
        // ========================================================

        var columnMap =
            new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var expectedHeader in ExpectedHeaderOrder)
        {
            var index = Array.FindIndex(
                headers,
                header => string.Equals(
                    NormalizeHeader(header),
                    expectedHeader,
                    StringComparison.Ordinal));

            if (index < 0)
            {
                index = Array.FindIndex(
                    headers,
                    header => string.Equals(
                        NormalizeHeader(header),
                        expectedHeader,
                        StringComparison.OrdinalIgnoreCase));
            }

            columnMap.Add(expectedHeader, index + 1);
        }

        // ========================================================
        // Find last row
        // ========================================================

        var lastRowNumber =
            worksheet
                .LastRowUsed()
                ?.RowNumber()
            ?? headerRowNumber;

        if (lastRowNumber <= headerRowNumber)
        {
            return;
        }

        // ========================================================
        // Read XLSX rows
        // ========================================================

        for (
            var rowNumber = headerRowNumber + 1;
            rowNumber <= lastRowNumber;
            rowNumber++)
        {
            var row =
                worksheet.Row(rowNumber);

            if (row.IsEmpty())
            {
                continue;
            }

            // ====================================================
            // Create record
            // ====================================================

            var record = new UploadBORequest
            {
                SESSION_ID =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "SESSION_ID"),

                BO_OPER_ID =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "BO_OPER_ID"),

                EP_STTL_DATE =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "EP_STTL_DATE"),

                RUN_DATE =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "RUN_DATE"),

                // =================================================
                // IMPORTANT:
                // TRX_TYPE is column 5
                // =================================================

                TRX_TYPE =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "TRX_TYPE"),

                MESSAGE_TYPE =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "MESSAGE_TYPE"),

                CLR_STATUS =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "CLR_STATUS"),

                CONTRACT_TYPE =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "CONTRACT_TYPE"),

                CARD_NUMBER =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "CARD_NUMBER"),

                ACCOUNT_NUMBER =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "ACCOUNT_NUMBER"),

                SENDER_ACCOUNT_NUMBER =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "SENDER_ACCOUNT_NUMBER"),

                AUTH_CODE =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "AUTH_CODE"),

                ARN =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "ARN"),

                TRANS_DATE =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "TRANS_DATE"),

                CLR_TXN_AMOUNT =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "CLR_TXN_AMOUNT"),

                TXN_CURRENCY =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "TXN_CURRENCY"),

                BILL_AMT =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "BILL_AMT"),

                ACCT_CURR =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "ACCT_CURR"),

                STTL_AMOUNT =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "STTL_AMOUNT"),

                ST_REV =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "ST_REV"),

                MATCH_STATUS =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "MATCH_STATUS"),

                AUTH_ID =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "AUTH_ID"),

                MCC =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "MCC"),

                MERCHANT_NUMBER =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "MERCHANT_NUMBER"),

                TERMINAL_NUMBER =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "TERMINAL_NUMBER"),

                MERCHANT_NAME =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "MERCHANT_NAME"),

                MERCHANT_CITY =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "MERCHANT_CITY"),

                MERCHANT_COUNTRY =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "MERCHANT_COUNTRY"),

                AUTH_OPR_ID =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "AUTH_OPR_ID"),

                BASE_II_ID =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "BASE_II_ID"),

                TRANSACTION_DATE =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "TRANSACTION_DATE"),

                AUTH_CARD_NUMBER =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "AUTH_CARD_NUMBER"),

                REVERSAL_FLAG =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "REVERSAL_FLAG"),

                TXN_AMOUNT =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "TXN_AMOUNT"),

                AUTH_CURRENCY =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "AUTH_CURRENCY"),

                BILLING_AMOUNT =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "BILLING_AMOUNT"),

                FEES =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "FEES"),

                BILLING_CURRENCY =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "BILLING_CURRENCY"),

                STATUS =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "STATUS"),

                // =================================================
                // IMPORTANT:
                // TXN_TYPE is column 40
                //
                // This is NOT TRX_TYPE.
                // =================================================

                TXN_TYPE =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "TXN_TYPE"),

                AUTH_MESSAGE_TYPE =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "AUTH_MESSAGE_TYPE"),

                AUTH_MCC =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "AUTH_MCC"),

                AUTH_MID =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "AUTH_MID"),

                AUTH_MERCHANT_NAME =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "AUTH_MERCHANT_NAME"),

                AUTH_CITY =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "AUTH_CITY"),

                AUTH_COUNTRY =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "AUTH_COUNTRY"),

                AUTH_TID =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "AUTH_TID"),

                AUTH_ACCT_UMBER =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "AUTH_ACCT_UMBER"),

                POS_COND_CODE =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "POS_COND_CODE"),

                UTRNNO =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "UTRNNO"),

                TRACE_NUMBER =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "TRACE_NUMBER"),

                TRACE_TO_CBS =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "TRACE_TO_CBS"),

                RRN =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "RRN"),

                AUTH =
                    GetString(
                        worksheet,
                        rowNumber,
                        columnMap,
                        "AUTH")
            };

            // ====================================================
            // Validate record
            // ====================================================

            NormalizeMatchingIdentifiers(record);

            if (IssuingUploadCleaning.ShouldRemove(record))
            {
                continue;
            }

            ValidateRecord(
                record,
                file.FileName);

            mergedRecords.Add(record);
        }

        await Task.CompletedTask;
    }


    // ============================================================
    // XLSX String Reader
    // ============================================================

    private static string GetString(
        IXLWorksheet worksheet,
        int rowNumber,
        Dictionary<string, int> columnMap,
        string header)
    {
        if (!columnMap.TryGetValue(
                header,
                out var columnNumber))
        {
            throw new InvalidDataException(
                $"Column '{header}' was not found.");
        }

        var cell =
            worksheet.Cell(
                rowNumber,
                columnNumber);

        return GetString(cell);
    }


    private static string GetString(
        IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return string.Empty;
        }

        /*
         * GetFormattedString() is intentional.
         *
         * It preserves what Excel displays instead of
         * converting everything through double.
         */
        return cell
            .GetFormattedString()
            .Trim();
    }

    private static void NormalizeMatchingIdentifiers(UploadBORequest record)
    {
        record.SESSION_ID = GlIdentifierConverter.Normalize(record.SESSION_ID);
        record.BO_OPER_ID = GlIdentifierConverter.Normalize(record.BO_OPER_ID);
        record.CARD_NUMBER = GlIdentifierConverter.Normalize(record.CARD_NUMBER);
        record.ACCOUNT_NUMBER = GlIdentifierConverter.Normalize(record.ACCOUNT_NUMBER);
        record.ARN = GlIdentifierConverter.Normalize(record.ARN);
        record.AUTH_CODE = GlIdentifierConverter.Normalize(record.AUTH_CODE);
        record.UTRNNO = GlIdentifierConverter.Normalize(record.UTRNNO);
        record.RRN = GlIdentifierConverter.Normalize(record.RRN);
    }


    // ============================================================
    // Record Validation
    // ============================================================

    private static void ValidateRecord(
        UploadBORequest record,
        string fileName)
    {
        if (string.IsNullOrWhiteSpace(
                record.SESSION_ID))
        {
            throw new InvalidDataException(
                $"File '{fileName}' contains an empty SESSION_ID.");
        }

        if (string.IsNullOrWhiteSpace(
                record.BO_OPER_ID))
        {
            throw new InvalidDataException(
                $"File '{fileName}' contains an empty BO_OPER_ID.");
        }

        // --------------------------------------------------------
        // Currency validation
        // --------------------------------------------------------

        ValidateCurrency(
            record.TXN_CURRENCY,
            "TXN_CURRENCY",
            fileName);

        ValidateCurrency(
            record.AUTH_CURRENCY,
            "AUTH_CURRENCY",
            fileName);

        ValidateCurrency(
            record.BILLING_CURRENCY,
            "BILLING_CURRENCY",
            fileName);
    }


    private static void ValidateCurrency(
        string? currency,
        string fieldName,
        string fileName)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            return;
        }

        if (currency.Length != 3)
        {
            throw new InvalidDataException(
                $"Invalid {fieldName} " +
                $"'{currency}' in file '{fileName}'.");
        }
    }


    // ============================================================
    // Header Normalization
    // ============================================================

    private static string NormalizeHeader(
        string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return string.Empty;
        }

        return header
            .Replace("\uFEFF", "") // BOM
            .Replace("\u00A0", " ") // non-breaking space
            .Replace("\t", " ")
            .Trim();
    }


    // ============================================================
    // Header Validation
    // ============================================================

    private static bool IsValidBOTransaction(
        string[]? headers,
        out string error)
    {
        error = string.Empty;

        if (headers == null ||
            headers.Length == 0)
        {
            error = "No headers were found.";

            return false;
        }

        // --------------------------------------------------------
        // Remove empty headers
        // --------------------------------------------------------

        var normalizedHeaders =
            headers
                .Select(NormalizeHeader)
                .ToArray();

        // --------------------------------------------------------
        // Only required headers are validated. Additional columns
        // are allowed and will not be inserted.
        // --------------------------------------------------------

        var actualHeaders =
            normalizedHeaders.ToHashSet(
                StringComparer.OrdinalIgnoreCase);

        var missingHeaders =
            ExpectedHeaders
                .Except(
                    actualHeaders,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (missingHeaders.Count > 0)
        {
            error =
                $"Missing required headers: " +
                $"{string.Join(", ", missingHeaders)}.";

            return false;
        }

        // --------------------------------------------------------
        // Header validation successful
        //
        // We intentionally do NOT require the columns to be in
        // exactly this order because XLSX/CSV may be reordered.
        //
        // Mapping is performed by header name.
        // --------------------------------------------------------

        return true;
    }
}
