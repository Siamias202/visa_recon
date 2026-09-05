using System.Text;
using Dapper;
using MySqlConnector;
using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.GLTransaction;
using VISA_RECON.API.Application.Helper;
using VISA_RECON.API.Application.Interfaces;
using VISA_RECON.API.Application.Interfaces.Repositories;

namespace VISA_RECON.API.Infrastructure.Repositories
{
    public class GLTransactionRepository : IGLTransactionRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        private const int TransactionBatchSize = 50_000;
        private const int SqlBatchSize = 1_000;
        private const int CommandTimeoutSeconds = 300;

        public GLTransactionRepository(
            IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<int> InsertBulkAsync(
            IEnumerable<UploadGLRequest> transactions)
        {
            if (transactions == null)
                return 0;

            // Keep the issuing-table rule at the repository boundary as well
            // as the upload service, so alternate application callers cannot
            // insert excluded transaction codes.
            //var items = transactions
            //    .Where(item => item is not null)
            //    .Where(item => !IssuingUploadCleaning.ShouldRemove(item))
            //    .ToList();

            var items = transactions
                .Where(item => item is not null)
                .ToList();

            if (items.Count == 0)
                return 0;

            await using var connection =
                (MySqlConnection)_connectionFactory.CreateConnection();

            await connection.OpenAsync();

            var uploadBatchId = await connection.ExecuteScalarAsync<long>(
                """
                INSERT INTO issuing_upload_batch
                    (source_type, status, total_rows)
                VALUES
                    ('CBS', 'PROCESSING', @TotalRows);
                SELECT LAST_INSERT_ID();
                """,
                new { TotalRows = items.Count },
                commandTimeout: CommandTimeoutSeconds);

            var totalInserted = 0;

            try
            {
                for (var transactionStart = 0;
                     transactionStart < items.Count;
                     transactionStart += TransactionBatchSize)
                {
                    var transactionItems = items
                        .Skip(transactionStart)
                        .Take(TransactionBatchSize)
                        .ToList();

                    await using var transaction =
                        await connection.BeginTransactionAsync();

                    try
                    {
                        for (var sqlStart = 0;
                             sqlStart < transactionItems.Count;
                             sqlStart += SqlBatchSize)
                        {
                            var batch = transactionItems
                                .Skip(sqlStart)
                                .Take(SqlBatchSize)
                                .ToList();
                            var sql = new StringBuilder("""
                                INSERT INTO issuing_cbs_transactions
                                (
                                    upload_batch_id,
                                    account_no,
                                    posting_date,
                                    value_date,
                                    batch_id,
                                    posting_branch,
                                    unique_reference_no,
                                    debit_credit,
                                    amount,
                                    transaction_code,
                                    transaction_name,
                                    currency,
                                    time_stamp,
                                    unique_id,
                                    narrative_1,
                                    narrative_2,
                                    narrative_3,
                                    narrative_4,
                                    rrn,
                                    auth_code,
                                    reconciliation_currency,
                                    transaction_category,
                                    reconciliation_status,
                                    primary_match_key,
                                    secondary_match_key
                                )
                                VALUES
                                """);
                            var parameters = new DynamicParameters();

                            for (var i = 0; i < batch.Count; i++)
                            {
                                if (i > 0)
                                    sql.Append(',');

                                var item = batch[i];
                                var classification =
                                    IssuingTransactionClassification.ClassifyCbs(
                                        item.AccountNo);
                                var primaryKey =
                                    IssuingTransactionClassification.CreatePrimaryKey(
                                        classification,
                                        item.UniqueReferenceNo,
                                        item.RRN,
                                        item.AuthCode,
                                        item.Amount);
                                var secondaryKey =
                                    IssuingTransactionClassification.CreateSecondaryKey(
                                        classification,
                                        item.UniqueReferenceNo,
                                        item.RRN,
                                        item.AuthCode,
                                        item.Amount);

                                sql.Append($"""
                                    (
                                        @UploadBatchId{i}, @AccountNo{i},
                                        @PostingDate{i}, @ValueDate{i}, @BatchId{i},
                                        @PostingBranch{i}, @UniqueReferenceNo{i},
                                        @DebitCredit{i}, @Amount{i},
                                        @TransactionCode{i}, @TransactionName{i},
                                        @Currency{i}, @TimeStamp{i}, @UniqueId{i},
                                        @Narrative1{i}, @Narrative2{i},
                                        @Narrative3{i}, @Narrative4{i}, @Rrn{i},
                                        @AuthCode{i}, @ReconCurrency{i}, @Category{i},
                                        'PENDING', @PrimaryKey{i}, @SecondaryKey{i}
                                    )
                                    """);

                                parameters.Add($"UploadBatchId{i}", uploadBatchId);
                                parameters.Add($"AccountNo{i}", TrimValue(item.AccountNo));
                                parameters.Add($"PostingDate{i}", item.PostingDate);
                                parameters.Add($"ValueDate{i}", item.ValueDate);
                                parameters.Add($"BatchId{i}", TrimValue(item.BatchId));
                                parameters.Add($"PostingBranch{i}", TrimValue(item.PostingBranch));
                                parameters.Add($"UniqueReferenceNo{i}", TrimValue(item.UniqueReferenceNo));
                                parameters.Add($"DebitCredit{i}", TrimValue(item.DebitCredit));
                                parameters.Add($"Amount{i}", item.Amount);
                                parameters.Add($"TransactionCode{i}", TrimValue(item.TransactionCode));
                                parameters.Add($"TransactionName{i}", TrimValue(item.TransactionName));
                                parameters.Add($"Currency{i}", TrimValue(item.Currency));
                                parameters.Add($"TimeStamp{i}", item.TimeStamp);
                                parameters.Add($"UniqueId{i}", TrimValue(item.UniqueId));
                                parameters.Add($"Narrative1{i}", TrimValue(item.Narrative1));
                                parameters.Add($"Narrative2{i}", TrimValue(item.Narrative2));
                                parameters.Add($"Narrative3{i}", TrimValue(item.Narrative3));
                                parameters.Add($"Narrative4{i}", TrimValue(item.Narrative4));
                                parameters.Add($"Rrn{i}", TrimValue(item.RRN));
                                parameters.Add($"AuthCode{i}", TrimValue(item.AuthCode));
                                parameters.Add($"ReconCurrency{i}", classification.Currency);
                                parameters.Add($"Category{i}", classification.Category);
                                parameters.Add($"PrimaryKey{i}", primaryKey);
                                parameters.Add($"SecondaryKey{i}", secondaryKey);
                            }

                            totalInserted += await connection.ExecuteAsync(
                                sql.ToString(),
                                parameters,
                                transaction,
                                CommandTimeoutSeconds);
                        }

                        await transaction.CommitAsync();
                    }
                    catch
                    {
                        try { await transaction.RollbackAsync(); } catch { }
                        throw;
                    }
                }

                await connection.ExecuteAsync(
                    """
                    UPDATE issuing_upload_batch
                    SET status = 'COMPLETED', completed_at = @CompletedAt,
                        accepted_rows = @AcceptedRows
                    WHERE id = @UploadBatchId;
                    """,
                    new
                    {
                        UploadBatchId = uploadBatchId,
                        CompletedAt = DateTime.UtcNow,
                        AcceptedRows = totalInserted
                    },
                    commandTimeout: CommandTimeoutSeconds);

                return totalInserted;
            }
            catch (Exception ex)
            {
                await CleanupFailedUploadAsync(uploadBatchId, ex.Message);
                throw new InvalidOperationException(
                    $"GL upload batch {uploadBatchId} failed after " +
                    $"{totalInserted} inserted rows. Error: {ex.Message}",
                    ex);
            }
        }

        private async Task CleanupFailedUploadAsync(long uploadBatchId, string error)
        {
            try
            {
                await using var connection =
                    (MySqlConnection)_connectionFactory.CreateConnection();
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync();

                await connection.ExecuteAsync(
                    "DELETE FROM issuing_cbs_transactions WHERE upload_batch_id = @UploadBatchId;",
                    new { UploadBatchId = uploadBatchId },
                    transaction,
                    CommandTimeoutSeconds);
                await connection.ExecuteAsync(
                    """
                    UPDATE issuing_upload_batch
                    SET status = 'FAILED', completed_at = @CompletedAt,
                        accepted_rows = 0, rejected_rows = total_rows,
                        error_message = @ErrorMessage
                    WHERE id = @UploadBatchId;
                    """,
                    new
                    {
                        UploadBatchId = uploadBatchId,
                        CompletedAt = DateTime.UtcNow,
                        ErrorMessage = error.Length <= 4000 ? error : error[..4000]
                    },
                    transaction,
                    CommandTimeoutSeconds);

                await transaction.CommitAsync();
            }
            catch
            {
                // Preserve the original upload exception.
            }
        }

        private static string? TrimValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
        }

        public async Task<PagedResponse<GLTransactionDetailsResponse>>
            GetGLTransactionDetailsListAsync(
                GLTransactionRequest request)
        {
            using var connection =
                _connectionFactory.CreateConnection();

            var page = Math.Max(request.Page, 1);
            var pageSize = Math.Clamp(request.PageSize, 1, 500);
            var offset = (page - 1) * pageSize;
            var currency = IssuingReconciliationFilter.NormalizeCurrency(
                request.Currency);
            var category = IssuingReconciliationFilter.NormalizeCategory(
                request.Category);
            var accountNumbers =
                IssuingReconciliationFilter.ResolveAccountNumbers(
                    request.AccountNumber,
                    currency,
                    category);

            const string sql = @"
                SELECT
                    id AS Id,
                    account_no AS AccountNo,
                    DATE(posting_date) AS PostingDate,
                    DATE(value_date) AS ValueDate,

                    TRIM(batch_id) AS BatchId,
                    TRIM(posting_branch) AS PostingBranch,
                    TRIM(unique_reference_no) AS UniqueReferenceNo,
                    TRIM(debit_credit) AS DebitCredit,
                    amount AS Amount,
                    TRIM(transaction_code) AS TransactionCode,
                    TRIM(transaction_name) AS TransactionName,
                    TRIM(currency) AS Currency,
                    time_stamp AS TimeStamp,
                    TRIM(unique_id) AS UniqueId,
                    TRIM(narrative_1) AS Narrative1,
                    TRIM(narrative_2) AS Narrative2,
                    TRIM(narrative_3) AS Narrative3,
                    TRIM(narrative_4) AS Narrative4,
                    TRIM(rrn) AS RRN,
                    TRIM(auth_code) AS AuthCode,
                    upload_batch_id AS UploadBatchId,
                    uploaded_at AS UploadedAt,
                    reconciliation_currency AS ReconciliationCurrency,
                    transaction_category AS TransactionCategory,
                    reconciliation_status AS ReconciliationStatus,
                    matched_at AS MatchedAt,
                    match_rule AS MatchRule
                FROM issuing_cbs_transactions
                WHERE
                    (
                        @SearchQuery IS NULL
                        OR @SearchQuery = ''
                        OR CAST(account_no AS CHAR)
                            LIKE CONCAT('%', @SearchQuery, '%')
                        OR batch_id
                            LIKE CONCAT('%', @SearchQuery, '%')
                        OR unique_reference_no
                            LIKE CONCAT('%', @SearchQuery, '%')
                    )
                AND
                    (
                        @AccountFilterCount = 0
                        OR account_no IN @AccountNumbers
                    )
                ORDER BY
                    CASE
                        WHEN @SortBy = 'posting_date'
                         AND @SortDirection = 'asc'
                        THEN posting_date
                    END ASC,

                    CASE
                        WHEN @SortBy = 'posting_date'
                         AND @SortDirection = 'desc'
                        THEN posting_date
                    END DESC,

                    CASE
                        WHEN @SortBy = 'amount'
                         AND @SortDirection = 'asc'
                        THEN amount
                    END ASC,

                    CASE
                        WHEN @SortBy = 'amount'
                         AND @SortDirection = 'desc'
                        THEN amount
                    END DESC,

                    account_no ASC

                LIMIT @PageSize
                OFFSET @Offset;

                SELECT COUNT(*)
                FROM issuing_cbs_transactions
                WHERE
                    (
                        @SearchQuery IS NULL
                        OR @SearchQuery = ''
                        OR CAST(account_no AS CHAR)
                            LIKE CONCAT('%', @SearchQuery, '%')
                        OR batch_id
                            LIKE CONCAT('%', @SearchQuery, '%')
                        OR unique_reference_no
                            LIKE CONCAT('%', @SearchQuery, '%')
                    )
                AND
                    (
                        @AccountFilterCount = 0
                        OR account_no IN @AccountNumbers
                    );
            ";

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
                            request.SortBy?.Trim(),

                        SortDirection =
                            request.SortDirection?
                                .Trim()
                                .ToLowerInvariant(),

                        AccountFilterCount = accountNumbers.Length,

                        AccountNumbers = accountNumbers.Length == 0
                            ? [""]
                            : accountNumbers,

                        Offset = offset,

                        PageSize = pageSize
                    });

            var data =
                (await multi
                    .ReadAsync<GLTransactionDetailsResponse>())
                .ToList();

            var totalRecords =
                await multi.ReadFirstAsync<int>();

            return new PagedResponse<GLTransactionDetailsResponse>
            {
                Items = data,
                Page = page,
                PageSize = pageSize,
                TotalItems = totalRecords,
                TotalPages = totalRecords == 0
                    ? 0
                    : (int)Math.Ceiling(
                        (double)totalRecords / pageSize)
            };
        }
    }
}
