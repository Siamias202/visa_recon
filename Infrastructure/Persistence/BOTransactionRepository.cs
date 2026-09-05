using System.Globalization;
using Dapper;
using MySqlConnector;
using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.BOTransaction;
using VISA_RECON.API.Application.Helper;
using VISA_RECON.API.Application.Interfaces;
using VISA_RECON.API.Application.Interfaces.Repository;
using VISA_RECON.API.Application.Mappings.GLMappingsHelper;

namespace VISA_RECON.API.Infrastructure.Persistence
{
    public class BOTransactionRepository : IBOTransactionRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        // ============================================================
        // CONFIGURATION
        // ============================================================

        // Logical transaction batch.
        // 50,000 records will be committed together.
        private const int TransactionBatchSize = 50_000;

        // Actual SQL INSERT size.
        //
        // DO NOT make this 50,000 because 50,000 x 54 parameters
        // creates a very large SQL command.
        private const int SqlBatchSize = 1_000;

        public BOTransactionRepository(
            IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        // ============================================================
        // INSERT
        // ============================================================

        public async Task<int> InsertBulkAsync(
            IEnumerable<UploadBORequest> transactions)
        {
            if (transactions == null)
                return 0;

            var totalInserted = 0;

            var batch = new List<UploadBORequest>(
                TransactionBatchSize);

            foreach (var item in transactions)
            {
                if (item == null)
                    continue;

                batch.Add(item);

                if (batch.Count >= TransactionBatchSize)
                {
                    totalInserted +=
                        await InsertBatchAsync(batch);

                    batch.Clear();
                }
            }

            // Insert remaining records.
            if (batch.Count > 0)
            {
                totalInserted +=
                    await InsertBatchAsync(batch);
            }

            return totalInserted;
        }

        // ============================================================
        // INSERT ONE 50K TRANSACTION BATCH
        // ============================================================

        private async Task<int> InsertBatchAsync(
            List<UploadBORequest> transactions)
        {
            if (transactions.Count == 0)
                return 0;

            await using var connection =
                (MySqlConnection)_connectionFactory.CreateConnection();

            await connection.OpenAsync();

            await using var transaction =
                await connection.BeginTransactionAsync();

            var insertedCount = 0;

            try
            {
                // ----------------------------------------------------
                // Split 50K into smaller SQL batches.
                // ----------------------------------------------------

                for (
                    var start = 0;
                    start < transactions.Count;
                    start += SqlBatchSize)
                {
                    var count =
                        Math.Min(
                            SqlBatchSize,
                            transactions.Count - start);

                    var sql =
                        BuildInsertSql(
                            transactions,
                            start,
                            count);

                    var parameters =
                        BuildParameters(
                            transactions,
                            start,
                            count);

                    var affected =
                        await connection.ExecuteAsync(
                            sql,
                            parameters,
                            transaction);

                    insertedCount += affected;
                }

                await transaction.CommitAsync();

                return insertedCount;
            }
            catch (Exception ex)
            {
                try
                {
                    await transaction.RollbackAsync();
                }
                catch
                {
                    // Preserve original exception.
                }

                throw new InvalidOperationException(
                    $"BO transaction upload failed. " +
                    $"Batch size: {transactions.Count}. " +
                    $"Records inserted before failure in this " +
                    $"transaction: {insertedCount}. " +
                    $"Error: {ex.Message}",
                    ex);
            }
        }

        // ============================================================
        // BUILD MULTI-ROW INSERT SQL
        // ============================================================

        private static string BuildInsertSql(
            List<UploadBORequest> transactions,
            int start,
            int count)
        {
            const string columns = """
                session_id,
                bo_oper_id,
                ep_sttl_date,
                run_date,
                trx_type,
                message_type,
                contract_type,
                card_number,
                account_number,
                sender_account_number,
                auth_code,
                arn,
                trans_date,
                txn_currency,
                sttl_amount,
                st_rev,
                merchant_name,
                merchant_country,
                transaction_date,
                reversal_flag,
                auth_message_type,
                utrnno,
                rrn
                """;

            var sql =
                new System.Text.StringBuilder();

            sql.AppendLine(
                "INSERT INTO issuing_bo_transaction");

            sql.AppendLine("(");
            sql.AppendLine(columns);
            sql.AppendLine(")");
            sql.AppendLine("VALUES");

            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                    sql.AppendLine(",");

                sql.Append("(");

                for (var column = 0; column < 23; column++)
                {
                    if (column > 0)
                        sql.Append(", ");

                    sql.Append(
                        $"@p_{i}_{column}");
                }

                sql.Append(")");
            }

            sql.Append(";");

            return sql.ToString();
        }

        // ============================================================
        // BUILD PARAMETERS
        // ============================================================

        private static DynamicParameters BuildParameters(
            List<UploadBORequest> transactions,
            int start,
            int count)
        {
            var parameters =
                new DynamicParameters();

            for (var i = 0; i < count; i++)
            {
                var item = transactions[start + i];

                var values = new object?[]
                {
                    NormalizeIdentifier(item.SESSION_ID),
                    NormalizeIdentifier(item.BO_OPER_ID),
                    ParseDate(item.EP_STTL_DATE, nameof(item.EP_STTL_DATE)),
                    ParseDate(item.RUN_DATE, nameof(item.RUN_DATE)),
                    TrimValue(item.TRX_TYPE),
                    TrimValue(item.MESSAGE_TYPE),
                    TrimValue(item.CONTRACT_TYPE),
                    NormalizeIdentifier(item.CARD_NUMBER),
                    NormalizeIdentifier(item.ACCOUNT_NUMBER),
                    TrimValue(item.SENDER_ACCOUNT_NUMBER),
                    TrimValue(item.AUTH_CODE),
                    NormalizeIdentifier(item.ARN),
                    ParseTimestamp(item.TRANS_DATE, nameof(item.TRANS_DATE)),
                    TrimValue(item.TXN_CURRENCY)?.ToUpperInvariant(),
                    ParseDecimal(item.STTL_AMOUNT, nameof(item.STTL_AMOUNT)),
                    ParseShort(item.ST_REV, nameof(item.ST_REV)),
                    TrimValue(item.MERCHANT_NAME),
                    TrimValue(item.MERCHANT_COUNTRY),
                    ParseTimestamp(item.TRANSACTION_DATE, nameof(item.TRANSACTION_DATE)),
                    ParseShort(item.REVERSAL_FLAG, nameof(item.REVERSAL_FLAG)),
                    TrimValue(item.AUTH_MESSAGE_TYPE),
                    TrimValue(item.UTRNNO),
                    TrimValue(item.RRN)
                };

                for (var column = 0; column < values.Length; column++)
                {
                    parameters.Add(
                        $"p_{i}_{column}",
                        values[column]);
                }
            }

            return parameters;
        }

        // ============================================================
        // STRING
        // ============================================================

        private static string? TrimValue(
            string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
        }

        private static string? NormalizeIdentifier(string? value)
        {
            var normalized = GlIdentifierConverter.Normalize(value);

            return string.IsNullOrWhiteSpace(normalized)
                ? null
                : normalized;
        }

        // ============================================================
        // BIGINT
        // ============================================================

        private static long? ParseLong(
            string? value,
            string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (!long.TryParse(
                    value.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var result))
            {
                throw new InvalidDataException(
                    $"Invalid BIGINT value '{value}' " +
                    $"for {fieldName}.");
            }

            return result;
        }

        // ============================================================
        // DECIMAL
        // ============================================================

        private static decimal? ParseDecimal(
            string? value,
            string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (!decimal.TryParse(
                    value.Trim(),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var result))
            {
                throw new InvalidDataException(
                    $"Invalid DECIMAL value '{value}' " +
                    $"for {fieldName}.");
            }

            return result;
        }

        // ============================================================
        // SMALLINT
        // ============================================================

        private static short? ParseShort(
            string? value,
            string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (!decimal.TryParse(
                    value.Trim(),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var parsed) ||
                parsed != decimal.Truncate(parsed) ||
                parsed < short.MinValue ||
                parsed > short.MaxValue)
            {
                throw new InvalidDataException(
                    $"Invalid SMALLINT value '{value}' " +
                    $"for {fieldName}.");
            }

            return (short)parsed;
        }

        // ============================================================
        // DATE
        // ============================================================

        private static DateTime? ParseDate(
            string? value,
            string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            value = value.Trim();

            var formats = new[]
            {
                "dd-MM-yyyy",
                "dd/MM/yyyy",
                "yyyy-MM-dd",
                "yyyy/MM/dd",

                "dd-MM-yyyy HH:mm:ss",
                "dd/MM/yyyy HH:mm:ss",
                "yyyy-MM-dd HH:mm:ss",
                "yyyy/MM/dd HH:mm:ss",

                "MM/dd/yyyy",
                "MM/dd/yyyy HH:mm:ss"
            };

            if (DateTime.TryParseExact(
                    value,
                    formats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var result))
            {
                return result.Date;
            }

            if (DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out result))
            {
                return result.Date;
            }

            throw new InvalidDataException(
                $"Invalid DATE value '{value}' " +
                $"for {fieldName}.");
        }

        // ============================================================
        // TIMESTAMP
        // ============================================================

        private static DateTime? ParseTimestamp(
            string? value,
            string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            value = value.Trim();

            var formats = new[]
            {
                "dd-MM-yyyy",
                "dd/MM/yyyy",
                "yyyy-MM-dd",
                "yyyy/MM/dd",
                "MM/dd/yyyy",

                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-dd HH:mm:ss.fff",
                "yyyy-MM-dd HH:mm:ss.ffffff",

                "dd-MM-yyyy HH:mm:ss",
                "dd-MM-yyyy HH:mm:ss.fff",
                "dd-MM-yyyy HH:mm:ss.ffffff",

                "dd/MM/yyyy HH:mm:ss",
                "dd/MM/yyyy HH:mm:ss.fff",

                "MM/dd/yyyy HH:mm:ss",
                "MM/dd/yyyy HH:mm:ss.fff",

                "yyyy-MM-ddTHH:mm:ss",
                "yyyy-MM-ddTHH:mm:ss.fff"
            };

            if (DateTime.TryParseExact(
                    value,
                    formats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var result))
            {
                return DateTime.SpecifyKind(
                    result,
                    DateTimeKind.Unspecified);
            }

            if (DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out result))
            {
                return DateTime.SpecifyKind(
                    result,
                    DateTimeKind.Unspecified);
            }

            throw new InvalidDataException(
                $"Invalid TIMESTAMP value '{value}' " +
                $"for {fieldName}.");
        }

        // ============================================================
        // GET / PAGINATION
        // ============================================================

        public async Task<PagedResponse<BOTransactionDetailsResponse>>
            GetBOTransactionDetailsListAsync(
                BOTransactionRequest request)
        {
            await using var connection =
                (MySqlConnection)_connectionFactory.CreateConnection();

            await connection.OpenAsync();

            var page =
                Math.Max(request.Page, 1);

            var pageSize =
                Math.Clamp(request.PageSize, 1, 500);

            var offset =
                (page - 1) * pageSize;

            var currency = IssuingReconciliationFilter.NormalizeCurrency(
                request.Currency);

            var category = IssuingReconciliationFilter.NormalizeCategory(
                request.Category);

            // Validate the hierarchy. BO rows are filtered using their own
            // currency and transaction type rather than GL settlement accounts.
            _ = IssuingReconciliationFilter.ResolveAccountNumbers(
                null,
                currency,
                category);

            var categoryTransactionTypes =
                IssuingReconciliationFilter.ResolveBoTransactionTypes(
                    category);

            var accountNumber = currency is null && category is null
                && !string.IsNullOrWhiteSpace(request.AccountNumber)
                    ? request.AccountNumber.Trim()
                    : null;

            const string sql = """
                SELECT
                    session_id AS SESSION_ID,
                    bo_oper_id AS BO_OPER_ID,
                    ep_sttl_date AS EP_STTL_DATE,
                    run_date AS RUN_DATE,
                    TRIM(trx_type) AS TRX_TYPE,
                    TRIM(message_type) AS MESSAGE_TYPE,
                    TRIM(contract_type) AS CONTRACT_TYPE,
                    TRIM(card_number) AS CARD_NUMBER,
                    TRIM(account_number) AS ACCOUNT_NUMBER,
                    TRIM(sender_account_number)
                        AS SENDER_ACCOUNT_NUMBER,
                    TRIM(auth_code) AS AUTH_CODE,
                    TRIM(arn) AS ARN,
                    trans_date AS TRANS_DATE,
                    TRIM(txn_currency) AS TXN_CURRENCY,
                    sttl_amount AS STTL_AMOUNT,
                    st_rev AS ST_REV,
                    TRIM(merchant_name)
                        AS MERCHANT_NAME,
                    TRIM(merchant_country)
                        AS MERCHANT_COUNTRY,
                    transaction_date AS TRANSACTION_DATE,
                    reversal_flag AS REVERSAL_FLAG,
                    TRIM(auth_message_type)
                        AS AUTH_MESSAGE_TYPE,
                    TRIM(utrnno) AS UTRNNO,
                    TRIM(rrn) AS RRN

                FROM issuing_bo_transaction

                WHERE
                    (
                        @SearchQuery IS NULL
                        OR @SearchQuery = ''

                        OR CAST(session_id AS CHAR)
                            LIKE CONCAT('%', @SearchQuery, '%')

                        OR CAST(bo_oper_id AS CHAR)
                            LIKE CONCAT('%', @SearchQuery, '%')

                        OR account_number
                            LIKE CONCAT('%', @SearchQuery, '%')

                        OR card_number
                            LIKE CONCAT('%', @SearchQuery, '%')

                        OR arn
                            LIKE CONCAT('%', @SearchQuery, '%')

                        OR rrn
                            LIKE CONCAT('%', @SearchQuery, '%')

                        OR auth_code
                            LIKE CONCAT('%', @SearchQuery, '%')

                        OR utrnno
                            LIKE CONCAT('%', @SearchQuery, '%')
                    )

                AND (@Currency IS NULL
                     OR UPPER(TRIM(txn_currency)) = @Currency)
                AND (@CategoryFilterCount = 0
                     OR UPPER(TRIM(trx_type)) IN @CategoryTransactionTypes)
                AND (@AccountNumber IS NULL
                     OR account_number = @AccountNumber
                     OR sender_account_number = @AccountNumber)

                ORDER BY

                    CASE
                        WHEN @SortBy = 'ep_sttl_date'
                         AND @SortDirection = 'asc'
                        THEN ep_sttl_date
                    END ASC,

                    CASE
                        WHEN @SortBy = 'ep_sttl_date'
                         AND @SortDirection = 'desc'
                        THEN ep_sttl_date
                    END DESC,

                    CASE
                        WHEN @SortBy = 'run_date'
                         AND @SortDirection = 'asc'
                        THEN run_date
                    END ASC,

                    CASE
                        WHEN @SortBy = 'run_date'
                         AND @SortDirection = 'desc'
                        THEN run_date
                    END DESC,

                    CASE
                        WHEN @SortBy = 'transaction_date'
                         AND @SortDirection = 'asc'
                        THEN transaction_date
                    END ASC,

                    CASE
                        WHEN @SortBy = 'transaction_date'
                         AND @SortDirection = 'desc'
                        THEN transaction_date
                    END DESC,

                    session_id ASC

                LIMIT @PageSize
                OFFSET @Offset;

                SELECT COUNT(*)

                FROM issuing_bo_transaction

                WHERE
                    (
                        @SearchQuery IS NULL
                        OR @SearchQuery = ''

                        OR CAST(session_id AS CHAR)
                            LIKE CONCAT('%', @SearchQuery, '%')

                        OR CAST(bo_oper_id AS CHAR)
                            LIKE CONCAT('%', @SearchQuery, '%')

                        OR account_number
                            LIKE CONCAT('%', @SearchQuery, '%')

                        OR card_number
                            LIKE CONCAT('%', @SearchQuery, '%')

                        OR arn
                            LIKE CONCAT('%', @SearchQuery, '%')

                        OR rrn
                            LIKE CONCAT('%', @SearchQuery, '%')

                        OR auth_code
                            LIKE CONCAT('%', @SearchQuery, '%')

                        OR utrnno
                            LIKE CONCAT('%', @SearchQuery, '%')
                    )

                AND (@Currency IS NULL
                     OR UPPER(TRIM(txn_currency)) = @Currency)
                AND (@CategoryFilterCount = 0
                     OR UPPER(TRIM(trx_type)) IN @CategoryTransactionTypes)
                AND (@AccountNumber IS NULL
                     OR account_number = @AccountNumber
                     OR sender_account_number = @AccountNumber);
                """;

            using var multi =
                await connection.QueryMultipleAsync(
                    sql,
                    new
                    {
                        SearchQuery =
                            string.IsNullOrWhiteSpace(
                                request.SearchQuery)
                                ? null
                                : request.SearchQuery.Trim(),

                        SortBy =
                            request.SortBy?
                                .Trim()
                                .ToLowerInvariant(),

                        SortDirection =
                            request.SortDirection?
                                .Trim()
                                .ToLowerInvariant(),

                        Currency = currency,

                        CategoryFilterCount = categoryTransactionTypes.Length,

                        CategoryTransactionTypes =
                            categoryTransactionTypes.Length == 0
                            ? [""]
                            : categoryTransactionTypes,

                        AccountNumber = accountNumber,

                        Offset = offset,

                        PageSize = pageSize
                    });

            var data =
                (await multi
                    .ReadAsync<BOTransactionDetailsResponse>())
                .ToList();

            foreach (var item in data)
            {
                item.SESSION_ID =
                    GlIdentifierConverter.Normalize(item.SESSION_ID);
                item.BO_OPER_ID =
                    GlIdentifierConverter.Normalize(item.BO_OPER_ID);
                item.CARD_NUMBER =
                    GlIdentifierConverter.Normalize(item.CARD_NUMBER);
                item.ACCOUNT_NUMBER =
                    GlIdentifierConverter.Normalize(item.ACCOUNT_NUMBER);
                item.ARN =
                    GlIdentifierConverter.Normalize(item.ARN);
            }

            var totalRecords =
                await multi.ReadFirstAsync<int>();

            return new PagedResponse<BOTransactionDetailsResponse>
            {
                Items = data,

                Page = page,

                PageSize = pageSize,

                TotalItems = totalRecords,

                TotalPages =
                    totalRecords == 0
                        ? 0
                        : (int)Math.Ceiling(
                            (double)totalRecords / pageSize)
            };
        }
    }
}
