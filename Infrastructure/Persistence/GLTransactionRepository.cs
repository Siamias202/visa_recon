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

        private const int BatchSize = 5_0000;

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

            var totalInserted = 0;

            for (var batchStart = 0;
                 batchStart < items.Count;
                 batchStart += BatchSize)
            {
                var batch = items
                    .Skip(batchStart)
                    .Take(BatchSize)
                    .ToList();

                if (batch.Count == 0)
                    continue;

                await using var transaction =
                    await connection.BeginTransactionAsync();

                try
                {
                    var sql = new StringBuilder();

                    sql.Append(@"
                        INSERT INTO issuing_cbs_transactions
                        (
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
                            auth_code
                        )
                        VALUES ");

                    var parameters = new DynamicParameters();

                    for (var i = 0; i < batch.Count; i++)
                    {
                        var item = batch[i];

                        if (i > 0)
                            sql.Append(",");

                        sql.Append($@"
                            (
                                @AccountNo{i},
                                @PostingDate{i},
                                @ValueDate{i},
                                @BatchId{i},
                                @PostingBranch{i},
                                @UniqueReferenceNo{i},
                                @DebitCredit{i},
                                @Amount{i},
                                @TransactionCode{i},
                                @TransactionName{i},
                                @Currency{i},
                                @TimeStamp{i},
                                @UniqueId{i},
                                @Narrative1{i},
                                @Narrative2{i},
                                @Narrative3{i},
                                @Narrative4{i},
                                @RRN{i},
                                @AuthCode{i}
                            )");

                        parameters.Add(
                            $"AccountNo{i}",
                            TrimValue(item.AccountNo));

                        parameters.Add(
                            $"PostingDate{i}",
                            item.PostingDate);

                        parameters.Add(
                            $"ValueDate{i}",
                            item.ValueDate);

                        parameters.Add(
                            $"BatchId{i}",
                            TrimValue(item.BatchId));

                        parameters.Add(
                            $"PostingBranch{i}",
                            TrimValue(item.PostingBranch));

                        parameters.Add(
                            $"UniqueReferenceNo{i}",
                            TrimValue(item.UniqueReferenceNo));

                        parameters.Add(
                            $"DebitCredit{i}",
                            TrimValue(item.DebitCredit));

                        parameters.Add(
                            $"Amount{i}",
                            item.Amount);

                        parameters.Add(
                            $"TransactionCode{i}",
                            TrimValue(item.TransactionCode));

                        parameters.Add(
                            $"TransactionName{i}",
                            TrimValue(item.TransactionName));

                        parameters.Add(
                            $"Currency{i}",
                            TrimValue(item.Currency));

                        parameters.Add(
                            $"TimeStamp{i}",
                            item.TimeStamp);

                        parameters.Add(
                            $"UniqueId{i}",
                            TrimValue(item.UniqueId));

                        parameters.Add(
                            $"Narrative1{i}",
                            TrimValue(item.Narrative1));

                        parameters.Add(
                            $"Narrative2{i}",
                            TrimValue(item.Narrative2));

                        parameters.Add(
                            $"Narrative3{i}",
                            TrimValue(item.Narrative3));

                        parameters.Add(
                            $"Narrative4{i}",
                            TrimValue(item.Narrative4));

                        parameters.Add(
                            $"RRN{i}",
                            TrimValue(item.RRN));

                        parameters.Add(
                            $"AuthCode{i}",
                            TrimValue(item.AuthCode));
                    }

                    var inserted = await connection.ExecuteAsync(
                        sql.ToString(),
                        parameters,
                        transaction);

                    await transaction.CommitAsync();

                    totalInserted += inserted;
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

                    throw new Exception(
                        $"GL transaction batch failed. " +
                        $"Batch starting at record {batchStart + 1}, " +
                        $"batch size {batch.Count}. " +
                        $"Total successfully inserted before failure: " +
                        $"{totalInserted}. Error: {ex.Message}",
                        ex);
                }
            }

            return totalInserted;
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
                    TRIM(auth_code) AS AuthCode
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
