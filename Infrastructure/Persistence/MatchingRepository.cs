using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using MySqlConnector;
using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.BOTransaction;
using VISA_RECON.API.Application.DTOs.GLTransaction;
using VISA_RECON.API.Application.DTOs.Reconciliation;
using VISA_RECON.API.Application.Helper;
using VISA_RECON.API.Application.Interfaces;
using VISA_RECON.API.Application.Interfaces.Repository;

namespace VISA_RECON.API.Infrastructure.Persistence;

public sealed class MatchingRepository : IMatchingRepository
{
    private const int CommandTimeoutSeconds = 720;
    private const int ResultInsertBatchSize = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const string CbsSelectColumns = """
        c.id AS Id,
        c.account_no AS AccountNo,
        DATE(c.posting_date) AS PostingDate,
        DATE(c.value_date) AS ValueDate,
        TRIM(c.batch_id) AS BatchId,
        TRIM(c.posting_branch) AS PostingBranch,
        TRIM(c.unique_reference_no) AS UniqueReferenceNo,
        TRIM(c.debit_credit) AS DebitCredit,
        c.amount AS Amount,
        TRIM(c.transaction_code) AS TransactionCode,
        TRIM(c.transaction_name) AS TransactionName,
        TRIM(c.currency) AS Currency,
        c.time_stamp AS TimeStamp,
        TRIM(c.unique_id) AS UniqueId,
        TRIM(c.narrative_1) AS Narrative1,
        TRIM(c.narrative_2) AS Narrative2,
        TRIM(c.narrative_3) AS Narrative3,
        TRIM(c.narrative_4) AS Narrative4,
        TRIM(c.rrn) AS RRN,
        TRIM(c.auth_code) AS AuthCode,
        DATE(c.posting_date) AS ReconciliationBusinessDate
        """;

    private const string BoSelectColumns = """
        b.session_id AS SESSION_ID,
        b.id AS Id,
        b.bo_oper_id AS BO_OPER_ID,
        b.ep_sttl_date AS EP_STTL_DATE,
        b.run_date AS RUN_DATE,
        TRIM(b.trx_type) AS TRX_TYPE,
        TRIM(b.message_type) AS MESSAGE_TYPE,
        TRIM(b.contract_type) AS CONTRACT_TYPE,
        TRIM(b.card_number) AS CARD_NUMBER,
        TRIM(b.account_number) AS ACCOUNT_NUMBER,
        TRIM(b.sender_account_number) AS SENDER_ACCOUNT_NUMBER,
        TRIM(b.auth_code) AS AUTH_CODE,
        TRIM(b.arn) AS ARN,
        b.trans_date AS TRANS_DATE,
        TRIM(b.txn_currency) AS TXN_CURRENCY,
        b.sttl_amount AS STTL_AMOUNT,
        b.st_rev AS ST_REV,
        TRIM(b.merchant_name) AS MERCHANT_NAME,
        TRIM(b.merchant_country) AS MERCHANT_COUNTRY,
        b.transaction_date AS TRANSACTION_DATE,
        b.reversal_flag AS REVERSAL_FLAG,
        TRIM(b.auth_message_type) AS AUTH_MESSAGE_TYPE,
        TRIM(b.utrnno) AS UTRNNO,
        TRIM(b.rrn) AS RRN,
        COALESCE(
            DATE(b.trans_date),
            DATE(b.transaction_date),
            DATE(b.ep_sttl_date),
            DATE(b.run_date)
        ) AS ReconciliationBusinessDate
        """;

    private const string EligibleBoCondition = """
            NOT EXISTS
            (
                SELECT 1
                FROM issuing_reversal_transaction r
                WHERE r.run_id = @RunId
                  AND (r.original_bo_transaction_id = b.id
                       OR r.reversal_bo_transaction_id = b.id)
            )
            """;

    private const string CleanCbsCondition = """
            UPPER(TRIM(COALESCE(c.transaction_code, ''))) NOT IN
                ('020', 'R07', 'R10', 'R26', 'R28', 'R33', 'R34', 'R44', 'R46')
            """;

    private const string CleanBoCondition = """
            UPPER(TRIM(COALESCE(b.trx_type, ''))) NOT IN
                ('PURCHASE RETURN (CREDIT)', 'PAYMENT TRANSACTION', 'P2P CREDIT')
            AND UPPER(TRIM(COALESCE(b.message_type, ''))) <> 'REPRESENTMENT'
            AND NOT
            (
                UPPER(TRIM(COALESCE(b.trx_type, ''))) <> 'ATM CASH WITHDRAWAL'
                AND b.st_rev = 1
            )
            """;


    private const string AgeBucketExpression = """
        CASE
            WHEN business_date IS NULL THEN 'UNKNOWN'
            WHEN business_date > DATE_SUB(@AsOfDate, INTERVAL 1 MONTH)
                THEN '<1 month'
            WHEN business_date > DATE_SUB(@AsOfDate, INTERVAL 3 MONTH)
                THEN '1-3 months'
            WHEN business_date > DATE_SUB(@AsOfDate, INTERVAL 6 MONTH)
                THEN '3-6 months'
            WHEN business_date > DATE_SUB(@AsOfDate, INTERVAL 12 MONTH)
                THEN '6-12 months'
            ELSE '>12 months'
        END
        """;

    private readonly IDbConnectionFactory _connectionFactory;

    public MatchingRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ReconciliationResultResponse> RunMatchingAsync()
    {
        await using var connection =
            (MySqlConnection)_connectionFactory.CreateConnection();

        await connection.OpenAsync();

        const string reconciliationLockName =
            "visa_recon:issuing_reconciliation";

        var lockAcquired = await connection.ExecuteScalarAsync<int>(
            "SELECT GET_LOCK(@LockName, 0);",
            new { LockName = reconciliationLockName },
            commandTimeout: CommandTimeoutSeconds);

        if (lockAcquired != 1)
        {
            throw new InvalidOperationException(
                "Another reconciliation run is already in progress.");
        }

        var startedAt = DateTime.UtcNow;

        const string createRunSql = """
            INSERT INTO issuing_reconciliation_run
            (
                started_at,
                status
            )
            VALUES
            (
                @StartedAt,
                'RUNNING'
            );
            """;

        await connection.ExecuteAsync(
            createRunSql,
            new { StartedAt = startedAt },
            commandTimeout: CommandTimeoutSeconds);

        var runId = await connection.ExecuteScalarAsync<long>(
            "SELECT LAST_INSERT_ID();",
            commandTimeout: CommandTimeoutSeconds);

        await using var transaction = await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted);

        try
        {
            var archiveReversalsSql = $"""
                INSERT INTO issuing_reversal_transaction
                (
                    run_id,
                    utrnno,
                    auth_code,
                    original_bo_transaction_id,
                    reversal_bo_transaction_id,
                    original_sttl_amount,
                    reversal_sttl_amount
                )
                WITH originals AS
                (
                    SELECT id, TRIM(utrnno) AS utrnno,
                           TRIM(auth_code) AS auth_code, sttl_amount,
                           ROW_NUMBER() OVER (
                               PARTITION BY TRIM(utrnno), TRIM(auth_code)
                               ORDER BY id) AS pair_number
                    FROM issuing_bo_transaction b
                    WHERE b.reversal_flag = 0
                      AND ({CleanBoCondition})
                      AND NULLIF(TRIM(utrnno), '') IS NOT NULL
                      AND NULLIF(TRIM(auth_code), '') IS NOT NULL
                ),
                reversals AS
                (
                    SELECT id, TRIM(utrnno) AS utrnno,
                           TRIM(auth_code) AS auth_code, sttl_amount,
                           ROW_NUMBER() OVER (
                               PARTITION BY TRIM(utrnno), TRIM(auth_code)
                               ORDER BY id) AS pair_number
                    FROM issuing_bo_transaction b
                    WHERE b.reversal_flag = 1
                      AND ({CleanBoCondition})
                      AND NULLIF(TRIM(utrnno), '') IS NOT NULL
                      AND NULLIF(TRIM(auth_code), '') IS NOT NULL
                )
                SELECT @RunId, o.utrnno, o.auth_code, o.id, r.id,
                       o.sttl_amount, r.sttl_amount
                FROM originals o
                INNER JOIN reversals r
                    ON r.utrnno = o.utrnno
                   AND r.auth_code = o.auth_code
                   AND r.pair_number = o.pair_number;
                """;

            var reverseCount = await connection.ExecuteAsync(
                archiveReversalsSql,
                new { RunId = runId },
                transaction,
                CommandTimeoutSeconds);

            var buildMatchPairsSql = $"""
                DROP TEMPORARY TABLE IF EXISTS tmp_issuing_match;
                DROP TEMPORARY TABLE IF EXISTS tmp_issuing_primary_cbs;
                DROP TEMPORARY TABLE IF EXISTS tmp_issuing_primary_bo;

                CREATE TEMPORARY TABLE tmp_issuing_match
                (
                    cbs_id BIGINT NOT NULL PRIMARY KEY,
                    bo_id BIGINT NOT NULL,
                    match_rule VARCHAR(20) NOT NULL,
                    UNIQUE KEY ux_tmp_issuing_match_bo (bo_id)
                ) ENGINE = InnoDB;

                INSERT INTO tmp_issuing_match (cbs_id, bo_id, match_rule)
                WITH cbs_primary AS
                (
                    SELECT c.id, TRIM(c.unique_reference_no) AS utrnno,
                           TRIM(c.rrn) AS rrn, TRIM(c.auth_code) AS auth_code,
                           c.amount,
                           ROW_NUMBER() OVER (
                               PARTITION BY TRIM(c.unique_reference_no),
                                            TRIM(c.rrn), TRIM(c.auth_code), c.amount
                               ORDER BY c.id) AS match_sequence
                    FROM issuing_cbs_transactions c
                    WHERE ({CleanCbsCondition})
                      AND NULLIF(TRIM(c.unique_reference_no), '') IS NOT NULL
                      AND NULLIF(TRIM(c.rrn), '') IS NOT NULL
                      AND NULLIF(TRIM(c.auth_code), '') IS NOT NULL
                      AND c.amount IS NOT NULL
                ),
                bo_primary AS
                (
                    SELECT b.id, TRIM(b.utrnno) AS utrnno,
                           TRIM(b.rrn) AS rrn, TRIM(b.auth_code) AS auth_code,
                           b.sttl_amount,
                           ROW_NUMBER() OVER (
                               PARTITION BY TRIM(b.utrnno), TRIM(b.rrn),
                                            TRIM(b.auth_code), b.sttl_amount
                               ORDER BY b.id) AS match_sequence
                    FROM issuing_bo_transaction b
                    WHERE ({CleanBoCondition})
                      AND NULLIF(TRIM(b.utrnno), '') IS NOT NULL
                      AND NULLIF(TRIM(b.rrn), '') IS NOT NULL
                      AND NULLIF(TRIM(b.auth_code), '') IS NOT NULL
                      AND b.sttl_amount IS NOT NULL
                      AND ({EligibleBoCondition})
                )
                SELECT c.id, b.id, 'PRIMARY'
                FROM cbs_primary c
                INNER JOIN bo_primary b
                    ON b.utrnno = c.utrnno
                   AND b.rrn = c.rrn
                   AND b.auth_code = c.auth_code
                   AND b.sttl_amount = c.amount
                   AND b.match_sequence = c.match_sequence;

                -- MySQL cannot read the same temporary table twice within one
                -- statement. Snapshot the two primary-match ID sets separately
                -- before constructing the secondary matches.
                CREATE TEMPORARY TABLE tmp_issuing_primary_cbs
                (
                    cbs_id BIGINT NOT NULL PRIMARY KEY
                ) ENGINE = InnoDB;

                INSERT INTO tmp_issuing_primary_cbs (cbs_id)
                SELECT cbs_id FROM tmp_issuing_match;

                CREATE TEMPORARY TABLE tmp_issuing_primary_bo
                (
                    bo_id BIGINT NOT NULL PRIMARY KEY
                ) ENGINE = InnoDB;

                INSERT INTO tmp_issuing_primary_bo (bo_id)
                SELECT bo_id FROM tmp_issuing_match;

                INSERT INTO tmp_issuing_match (cbs_id, bo_id, match_rule)
                WITH cbs_secondary AS
                (
                    SELECT c.id, TRIM(c.auth_code) AS auth_code, c.amount,
                           ROW_NUMBER() OVER (
                               PARTITION BY TRIM(c.auth_code), c.amount
                               ORDER BY c.id) AS match_sequence
                    FROM issuing_cbs_transactions c
                    WHERE ({CleanCbsCondition})
                      AND NULLIF(TRIM(c.unique_reference_no), '') IS NULL
                      AND NULLIF(TRIM(c.rrn), '') IS NULL
                      AND NULLIF(TRIM(c.auth_code), '') IS NOT NULL
                      AND c.amount IS NOT NULL
                      AND NOT EXISTS (
                          SELECT 1
                          FROM tmp_issuing_primary_cbs m
                          WHERE m.cbs_id = c.id)
                ),
                bo_secondary AS
                (
                    SELECT b.id, TRIM(b.auth_code) AS auth_code, b.sttl_amount,
                           ROW_NUMBER() OVER (
                               PARTITION BY TRIM(b.auth_code), b.sttl_amount
                               ORDER BY b.id) AS match_sequence
                    FROM issuing_bo_transaction b
                    WHERE ({CleanBoCondition})
                      AND NULLIF(TRIM(b.utrnno), '') IS NULL
                      AND NULLIF(TRIM(b.rrn), '') IS NULL
                      AND NULLIF(TRIM(b.auth_code), '') IS NOT NULL
                      AND b.sttl_amount IS NOT NULL
                      AND ({EligibleBoCondition})
                      AND NOT EXISTS (
                          SELECT 1
                          FROM tmp_issuing_primary_bo m
                          WHERE m.bo_id = b.id)
                )
                SELECT c.id, b.id, 'SECONDARY'
                FROM cbs_secondary c
                INNER JOIN bo_secondary b
                    ON b.auth_code = c.auth_code
                   AND b.sttl_amount = c.amount
                   AND b.match_sequence = c.match_sequence;
                """;

            await connection.ExecuteAsync(
                buildMatchPairsSql,
                new { RunId = runId },
                transaction,
                CommandTimeoutSeconds);

            var matchedSql = $"""
                SELECT
                    {CbsSelectColumns},
                    {BoSelectColumns}
                FROM tmp_issuing_match AS m
                INNER JOIN issuing_cbs_transactions AS c ON c.id = m.cbs_id
                INNER JOIN issuing_bo_transaction AS b ON b.id = m.bo_id;
                """;

            var matched =
                (await connection.QueryAsync<
                    GLTransactionDetailsResponse,
                    BOTransactionDetailsResponse,
                    MatchedTransactionResponse>(
                    matchedSql,
                    (cbs, bo) => new MatchedTransactionResponse
                    {
                        CbsData = cbs,
                        BoData = bo
                    },
                    transaction: transaction,
                    splitOn: "SESSION_ID",
                    commandTimeout: CommandTimeoutSeconds))
                .ToList();

            var missingSql = $"""
                SELECT
                    {BoSelectColumns}
                FROM issuing_bo_transaction AS b
                LEFT JOIN tmp_issuing_match AS m ON m.bo_id = b.id
                WHERE m.bo_id IS NULL
                  AND ({CleanBoCondition})
                  AND ({EligibleBoCondition});

                SELECT
                    {CbsSelectColumns}
                FROM issuing_cbs_transactions AS c
                LEFT JOIN tmp_issuing_match AS m ON m.cbs_id = c.id
                WHERE m.cbs_id IS NULL
                  AND ({CleanCbsCondition});
                """;

            List<BOTransactionDetailsResponse> missingInCbs;
            List<GLTransactionDetailsResponse> missingInBo;

            using (var multi = await connection.QueryMultipleAsync(
                       missingSql,
                       new { RunId = runId },
                       transaction: transaction,
                       commandTimeout: CommandTimeoutSeconds))
            {
                missingInCbs =
                    (await multi.ReadAsync<BOTransactionDetailsResponse>())
                    .ToList();

                missingInBo =
                    (await multi.ReadAsync<GLTransactionDetailsResponse>())
                    .ToList();
            }

            await InsertResultRowsAsync(
                connection,
                transaction,
                matched.Select(item => new ResultInsertRow(
                    "MATCHED",
                    item.BoData.ReconciliationBusinessDate
                        ?? item.CbsData.ReconciliationBusinessDate,
                    Serialize(item.CbsData),
                    Serialize(item.BoData))),
                runId);

            await InsertResultRowsAsync(
                connection,
                transaction,
                missingInCbs.Select(bo => new ResultInsertRow(
                    "MISSING_IN_CBS",
                    bo.ReconciliationBusinessDate,
                    null,
                    Serialize(bo))),
                runId);

            await InsertResultRowsAsync(
                connection,
                transaction,
                missingInBo.Select(cbs => new ResultInsertRow(
                    "MISSING_IN_BO",
                    cbs.ReconciliationBusinessDate,
                    Serialize(cbs),
                    null)),
                runId);

            var completedAt = DateTime.UtcNow;

            const string completeRunSql = """
                UPDATE issuing_reconciliation_run
                SET
                    completed_at = @CompletedAt,
                    status = 'COMPLETED',
                    matched_count = @MatchedCount,
                    missing_in_cbs_count = @MissingInCbsCount,
                    missing_in_bo_count = @MissingInBoCount,
                    reverse_count = @ReverseCount,
                    error_message = NULL
                WHERE id = @RunId;
                """;

            await connection.ExecuteAsync(
                completeRunSql,
                new
                {
                    RunId = runId,
                    CompletedAt = completedAt,
                    MatchedCount = matched.Count,
                    MissingInCbsCount = missingInCbs.Count,
                    MissingInBoCount = missingInBo.Count,
                    ReverseCount = reverseCount
                },
                transaction,
                CommandTimeoutSeconds);

            await transaction.CommitAsync();

            try
            {
                await connection.ExecuteAsync(
                    "SELECT RELEASE_LOCK(@LockName);",
                    new { LockName = reconciliationLockName },
                    commandTimeout: CommandTimeoutSeconds);
            }
            catch
            {
                // The connection will release the named lock when disposed.
            }

            return new ReconciliationResultResponse
            {
                RunId = runId,
                StartedAt = startedAt,
                CompletedAt = completedAt,
                MatchedCount = matched.Count,
                MissingInCbsCount = missingInCbs.Count,
                MissingInBoCount = missingInBo.Count,
                ReverseCount = reverseCount,
                ReverseTransactionsArchived = reverseCount
            };
        }
        catch (Exception ex)
        {
            try
            {
                await transaction.RollbackAsync();
            }
            catch
            {
                // Preserve the reconciliation error.
            }

            try
            {
                const string failRunSql = """
                    UPDATE issuing_reconciliation_run
                    SET
                        completed_at = @CompletedAt,
                        status = 'FAILED',
                        error_message = @ErrorMessage
                    WHERE id = @RunId;
                    """;

                await connection.ExecuteAsync(
                    failRunSql,
                    new
                    {
                        RunId = runId,
                        CompletedAt = DateTime.UtcNow,
                        ErrorMessage = ex.Message
                    },
                    commandTimeout: CommandTimeoutSeconds);
            }
            catch
            {
                // Preserve the original reconciliation error.
            }

            try
            {
                await connection.ExecuteAsync(
                    "SELECT RELEASE_LOCK(@LockName);",
                    new { LockName = reconciliationLockName },
                    commandTimeout: CommandTimeoutSeconds);
            }
            catch
            {
                // Closing the connection also releases the named lock.
            }

            throw new InvalidOperationException(
                $"Reconciliation run {runId} was rolled back. Error: {ex.Message}",
                ex);
        }
    }

    public async Task<PagedResponse<ReconciliationStoredResultResponse>>
        GetResultsAsync(ReconciliationResultsRequest request)
    {
        if (request.RunId <= 0)
        {
            throw new InvalidDataException("RunId must be greater than zero.");
        }

        var status = NormalizeResultStatus(request.ReconciliationStatus);
        var currency = IssuingReconciliationFilter.NormalizeCurrency(
            request.Currency);
        var category = IssuingReconciliationFilter.NormalizeCategory(
            request.Category);
        var accountNumbers = IssuingReconciliationFilter.ResolveAccountNumbers(
            request.AccountNumber,
            currency,
            category);
        var categoryTransactionTypes =
            IssuingReconciliationFilter.ResolveBoTransactionTypes(category);
        var boAccountNumber = currency is null && category is null
            && !string.IsNullOrWhiteSpace(request.AccountNumber)
                ? request.AccountNumber.Trim()
                : null;
        var dateFrom = request.DateFrom?.Date;
        var dateTo = request.DateTo?.Date;

        if (dateFrom > dateTo)
        {
            throw new InvalidDataException(
                "DateFrom cannot be later than DateTo.");
        }

        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 500);
        var offset = (page - 1) * pageSize;

        await using var connection =
            (MySqlConnection)_connectionFactory.CreateConnection();

        await connection.OpenAsync();

        const string sql = """
            SELECT
                id AS Id,
                run_id AS RunId,
                reconciliation_status AS ReconciliationStatus,
                business_date AS BusinessDate,
                CAST(cbs_data AS CHAR) AS CbsDataJson,
                CAST(bo_data AS CHAR) AS BoDataJson,
                created_at AS CreatedAt
            FROM issuing_reconciliation_result
            WHERE run_id = @RunId
              AND (
                    @ReconciliationStatus IS NULL
                    OR reconciliation_status = @ReconciliationStatus
                  )
              AND (@DateFrom IS NULL OR business_date >= @DateFrom)
              AND (@DateTo IS NULL OR business_date <= @DateTo)
              AND
                  (
                      (
                          reconciliation_status = 'MISSING_IN_CBS'
                          AND (@Currency IS NULL OR UPPER(JSON_UNQUOTE(
                              JSON_EXTRACT(bo_data, '$.TXN_CURRENCY'))) = @Currency)
                          AND (@CategoryFilterCount = 0 OR UPPER(JSON_UNQUOTE(
                              JSON_EXTRACT(bo_data, '$.TRX_TYPE')))
                              IN @CategoryTransactionTypes)
                          AND (@BoAccountNumber IS NULL
                               OR JSON_UNQUOTE(JSON_EXTRACT(
                                   bo_data, '$.ACCOUNT_NUMBER')) = @BoAccountNumber
                               OR JSON_UNQUOTE(JSON_EXTRACT(
                                   bo_data, '$.SENDER_ACCOUNT_NUMBER')) = @BoAccountNumber)
                      )
                      OR
                      (
                          reconciliation_status <> 'MISSING_IN_CBS'
                          AND
                          (
                              @AccountFilterCount = 0
                              OR JSON_UNQUOTE(JSON_EXTRACT(
                                  cbs_data, '$.AccountNo')) IN @AccountNumbers
                              OR JSON_UNQUOTE(JSON_EXTRACT(
                                  bo_data, '$.ACCOUNT_NUMBER')) IN @AccountNumbers
                              OR JSON_UNQUOTE(JSON_EXTRACT(
                                  bo_data, '$.SENDER_ACCOUNT_NUMBER')) IN @AccountNumbers
                          )
                      )
                  )
            ORDER BY id
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*)
            FROM issuing_reconciliation_result
            WHERE run_id = @RunId
              AND (
                    @ReconciliationStatus IS NULL
                    OR reconciliation_status = @ReconciliationStatus
                  )
              AND (@DateFrom IS NULL OR business_date >= @DateFrom)
              AND (@DateTo IS NULL OR business_date <= @DateTo)
              AND
                  (
                      (
                          reconciliation_status = 'MISSING_IN_CBS'
                          AND (@Currency IS NULL OR UPPER(JSON_UNQUOTE(
                              JSON_EXTRACT(bo_data, '$.TXN_CURRENCY'))) = @Currency)
                          AND (@CategoryFilterCount = 0 OR UPPER(JSON_UNQUOTE(
                              JSON_EXTRACT(bo_data, '$.TRX_TYPE')))
                              IN @CategoryTransactionTypes)
                          AND (@BoAccountNumber IS NULL
                               OR JSON_UNQUOTE(JSON_EXTRACT(
                                   bo_data, '$.ACCOUNT_NUMBER')) = @BoAccountNumber
                               OR JSON_UNQUOTE(JSON_EXTRACT(
                                   bo_data, '$.SENDER_ACCOUNT_NUMBER')) = @BoAccountNumber)
                      )
                      OR
                      (
                          reconciliation_status <> 'MISSING_IN_CBS'
                          AND
                          (
                              @AccountFilterCount = 0
                              OR JSON_UNQUOTE(JSON_EXTRACT(
                                  cbs_data, '$.AccountNo')) IN @AccountNumbers
                              OR JSON_UNQUOTE(JSON_EXTRACT(
                                  bo_data, '$.ACCOUNT_NUMBER')) IN @AccountNumbers
                              OR JSON_UNQUOTE(JSON_EXTRACT(
                                  bo_data, '$.SENDER_ACCOUNT_NUMBER')) IN @AccountNumbers
                          )
                      )
                  );
            """;

        using var multi = await connection.QueryMultipleAsync(
            sql,
            new
            {
                request.RunId,
                ReconciliationStatus = status,
                Currency = currency,
                CategoryFilterCount = categoryTransactionTypes.Length,
                CategoryTransactionTypes =
                    categoryTransactionTypes.Length == 0
                        ? [""]
                        : categoryTransactionTypes,
                BoAccountNumber = boAccountNumber,
                DateFrom = dateFrom,
                DateTo = dateTo,
                AccountFilterCount = accountNumbers.Length,
                AccountNumbers = accountNumbers.Length == 0
                    ? [""]
                    : accountNumbers,
                PageSize = pageSize,
                Offset = offset
            },
            commandTimeout: CommandTimeoutSeconds);

        var rows = (await multi.ReadAsync<StoredResultDbRow>()).ToList();
        var totalItems = await multi.ReadFirstAsync<int>();

        return new PagedResponse<ReconciliationStoredResultResponse>
        {
            Items = rows.Select(MapStoredResult).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalItems == 0
                ? 0
                : (int)Math.Ceiling((double)totalItems / pageSize)
        };
    }

    public async Task<MonthlyUnresolvedResponse> GetMonthlyUnresolvedAsync(
        MonthlyUnresolvedRequest request)
    {
        var status = NormalizeUnresolvedStatus(request.ReconciliationStatus);
        var ageBucket = NormalizeAgeBucket(request.AgeBucket);
        var asOfDate = (request.AsOfDate ?? DateTime.UtcNow).Date;
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 500);
        var offset = (page - 1) * pageSize;

        await using var connection =
            (MySqlConnection)_connectionFactory.CreateConnection();

        await connection.OpenAsync();

        const string findRunSql = """
            SELECT id
            FROM issuing_reconciliation_run
            WHERE status = 'COMPLETED'
              AND (@RunId = 0 OR id = @RunId)
            ORDER BY id DESC
            LIMIT 1;
            """;

        var runId = await connection.QueryFirstOrDefaultAsync<long?>(
            findRunSql,
            new { request.RunId },
            commandTimeout: CommandTimeoutSeconds);

        if (!runId.HasValue)
        {
            throw new InvalidDataException(
                request.RunId == 0
                    ? "No completed reconciliation run was found."
                    : $"Completed reconciliation run {request.RunId} was not found.");
        }

        var sql = $"""
            WITH bucketed AS
            (
                SELECT
                    id AS Id,
                    run_id AS RunId,
                    reconciliation_status AS ReconciliationStatus,
                    business_date AS BusinessDate,
                    {AgeBucketExpression} AS AgeBucket,
                    CAST(cbs_data AS CHAR) AS CbsDataJson,
                    CAST(bo_data AS CHAR) AS BoDataJson,
                    created_at AS CreatedAt
                FROM issuing_reconciliation_result
                WHERE run_id = @SelectedRunId
                  AND reconciliation_status IN
                      ('MISSING_IN_CBS', 'MISSING_IN_BO')
            )
            SELECT *
            FROM bucketed
            WHERE (
                    @ReconciliationStatus IS NULL
                    OR ReconciliationStatus = @ReconciliationStatus
                  )
              AND (@AgeBucket IS NULL OR AgeBucket = @AgeBucket)
            ORDER BY Id
            LIMIT @PageSize OFFSET @Offset;

            WITH bucketed AS
            (
                SELECT
                    reconciliation_status AS ReconciliationStatus,
                    {AgeBucketExpression} AS AgeBucket
                FROM issuing_reconciliation_result
                WHERE run_id = @SelectedRunId
                  AND reconciliation_status IN
                      ('MISSING_IN_CBS', 'MISSING_IN_BO')
            )
            SELECT COUNT(*)
            FROM bucketed
            WHERE (
                    @ReconciliationStatus IS NULL
                    OR ReconciliationStatus = @ReconciliationStatus
                  )
              AND (@AgeBucket IS NULL OR AgeBucket = @AgeBucket);

            SELECT
                reconciliation_status AS ReconciliationStatus,
                {AgeBucketExpression} AS AgeBucket,
                COUNT(*) AS ItemCount
            FROM issuing_reconciliation_result
            WHERE run_id = @SelectedRunId
              AND reconciliation_status IN
                  ('MISSING_IN_CBS', 'MISSING_IN_BO')
              AND (
                    @ReconciliationStatus IS NULL
                    OR reconciliation_status = @ReconciliationStatus
                  )
            GROUP BY reconciliation_status, AgeBucket
            ORDER BY
                reconciliation_status,
                FIELD(
                    AgeBucket,
                    '<1 month',
                    '1-3 months',
                    '3-6 months',
                    '6-12 months',
                    '>12 months',
                    'UNKNOWN'
                );
            """;

        var parameters = new
        {
            SelectedRunId = runId.Value,
            AsOfDate = asOfDate,
            ReconciliationStatus = status,
            AgeBucket = ageBucket,
            PageSize = pageSize,
            Offset = offset
        };

        using var multi = await connection.QueryMultipleAsync(
            sql,
            parameters,
            commandTimeout: CommandTimeoutSeconds);

        var rows = (await multi.ReadAsync<StoredResultDbRow>()).ToList();
        var totalItems = await multi.ReadFirstAsync<int>();
        var summary = (await multi.ReadAsync<AgeBucketSummaryResponse>()).ToList();

        return new MonthlyUnresolvedResponse
        {
            RunId = runId.Value,
            AsOfDate = asOfDate,
            Summary = summary,
            Results = new PagedResponse<ReconciliationStoredResultResponse>
            {
                Items = rows.Select(MapStoredResult).ToList(),
                Page = page,
                PageSize = pageSize,
                TotalItems = totalItems,
                TotalPages = totalItems == 0
                    ? 0
                    : (int)Math.Ceiling((double)totalItems / pageSize)
            }
        };
    }

    public async Task<PagedResponse<IssuingReversalResponse>> GetReversalsAsync(
        IssuingReversalRequest request)
    {
        if (request.RunId <= 0)
        {
            throw new InvalidDataException("RunId must be greater than zero.");
        }

        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 500);
        var offset = (page - 1) * pageSize;

        const string sql = """
            SELECT id AS Id, run_id AS RunId, utrnno AS Utrnno,
                   auth_code AS AuthCode,
                   original_bo_transaction_id AS OriginalBoTransactionId,
                   reversal_bo_transaction_id AS ReversalBoTransactionId,
                   original_sttl_amount AS OriginalSettlementAmount,
                   reversal_sttl_amount AS ReversalSettlementAmount,
                   created_at AS CreatedAt
            FROM issuing_reversal_transaction
            WHERE run_id = @RunId
            ORDER BY id
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*)
            FROM issuing_reversal_transaction
            WHERE run_id = @RunId;
            """;

        await using var connection =
            (MySqlConnection)_connectionFactory.CreateConnection();
        await connection.OpenAsync();

        using var multi = await connection.QueryMultipleAsync(
            sql,
            new { request.RunId, PageSize = pageSize, Offset = offset },
            commandTimeout: CommandTimeoutSeconds);

        var items = (await multi.ReadAsync<IssuingReversalResponse>()).ToList();
        var totalItems = await multi.ReadFirstAsync<int>();

        return new PagedResponse<IssuingReversalResponse>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalItems == 0
                ? 0
                : (int)Math.Ceiling((double)totalItems / pageSize)
        };
    }

    private static async Task InsertResultRowsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        IEnumerable<ResultInsertRow> rows,
        long runId)
    {
        foreach (var batch in rows.Chunk(ResultInsertBatchSize))
        {
            var sql = new StringBuilder("""
                INSERT INTO issuing_reconciliation_result
                (
                    run_id,
                    reconciliation_status,
                    business_date,
                    cbs_data,
                    bo_data
                )
                VALUES
                """);

            var parameters = new DynamicParameters();

            for (var i = 0; i < batch.Length; i++)
            {
                if (i > 0)
                {
                    sql.Append(',');
                }

                sql.Append($"""
                    (
                        @RunId{i},
                        @Status{i},
                        @BusinessDate{i},
                        @CbsData{i},
                        @BoData{i}
                    )
                    """);

                parameters.Add($"RunId{i}", runId);
                parameters.Add($"Status{i}", batch[i].Status);
                parameters.Add($"BusinessDate{i}", batch[i].BusinessDate?.Date);
                parameters.Add($"CbsData{i}", batch[i].CbsDataJson);
                parameters.Add($"BoData{i}", batch[i].BoDataJson);
            }

            sql.Append(';');

            await connection.ExecuteAsync(
                sql.ToString(),
                parameters,
                transaction,
                CommandTimeoutSeconds);
        }
    }

    private static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static ReconciliationStoredResultResponse MapStoredResult(
        StoredResultDbRow row)
    {
        return new ReconciliationStoredResultResponse
        {
            Id = row.Id,
            RunId = row.RunId,
            ReconciliationStatus = row.ReconciliationStatus,
            BusinessDate = row.BusinessDate,
            AgeBucket = row.AgeBucket,
            CbsData = ParseJson(row.CbsDataJson),
            BoData = ParseJson(row.BoDataJson),
            CreatedAt = row.CreatedAt
        };
    }

    private static JsonElement? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string? NormalizeResultStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        var normalized = status.Trim().ToUpperInvariant();

        return normalized switch
        {
            "MATCHED" => normalized,
            "MISSING_IN_CBS" => normalized,
            "MISSING_IN_BO" => normalized,
            _ => throw new InvalidDataException(
                $"Unsupported reconciliation status '{status}'.")
        };
    }

    private static string? NormalizeUnresolvedStatus(string? status)
    {
        var normalized = NormalizeResultStatus(status);

        if (normalized is null
            or "MISSING_IN_CBS"
            or "MISSING_IN_BO")
        {
            return normalized;
        }

        throw new InvalidDataException(
            "Monthly unresolved status must be MISSING_IN_CBS or MISSING_IN_BO.");
    }

    private static string? NormalizeAgeBucket(string? ageBucket)
    {
        if (string.IsNullOrWhiteSpace(ageBucket))
        {
            return null;
        }

        return ageBucket.Trim().ToLowerInvariant() switch
        {
            "<1 month" => "<1 month",
            "1-3 months" => "1-3 months",
            "3-6 months" => "3-6 months",
            "6-12 months" => "6-12 months",
            ">12 months" => ">12 months",
            "unknown" => "UNKNOWN",
            _ => throw new InvalidDataException(
                $"Unsupported age bucket '{ageBucket}'.")
        };
    }

    private sealed record ResultInsertRow(
        string Status,
        DateTime? BusinessDate,
        string? CbsDataJson,
        string? BoDataJson);

    private sealed class StoredResultDbRow
    {
        public long Id { get; set; }
        public long RunId { get; set; }
        public string ReconciliationStatus { get; set; } = string.Empty;
        public DateTime? BusinessDate { get; set; }
        public string? AgeBucket { get; set; }
        public string? CbsDataJson { get; set; }
        public string? BoDataJson { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
