using System.Data;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.Reconciliation;
using VISA_RECON.API.Application.Helper;
using VISA_RECON.API.Application.Interfaces;
using VISA_RECON.API.Application.Interfaces.Repository;

namespace VISA_RECON.API.Infrastructure.Persistence;

public sealed class MatchingRepository : IMatchingRepository
{
    private const int CommandTimeoutSeconds = 1_800;
    private const string LockName = "visa_recon:issuing_reconciliation";

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

        var lockAcquired = await connection.ExecuteScalarAsync<int>(
            "SELECT GET_LOCK(@LockName, 0);",
            new { LockName },
            commandTimeout: CommandTimeoutSeconds);

        if (lockAcquired != 1)
        {
            throw new InvalidOperationException(
                "Another issuing reconciliation run is already in progress.");
        }

        long runId = 0;
        var stage = "initializing";

        try
        {
            var startedAt = DateTime.UtcNow;
            var reconciliationDate = DateTime.Today;
            var cutoffs = await GetCutoffsAsync(connection);

            stage = "creating the reconciliation run";
            await connection.ExecuteAsync(
                """
                INSERT INTO issuing_reconciliation_run
                (
                    reconciliation_date, started_at, status, run_type,
                    rule_version, cbs_cutoff_id, bo_cutoff_id
                )
                VALUES
                (
                    @ReconciliationDate, @StartedAt, 'RUNNING', 'AUTOMATIC',
                    @RuleVersion, @CbsCutoffId, @BoCutoffId
                );
                """,
                new
                {
                    ReconciliationDate = reconciliationDate,
                    StartedAt = startedAt,
                    RuleVersion = IssuingTransactionClassification.RuleVersion,
                    cutoffs.CbsCutoffId,
                    cutoffs.BoCutoffId
                },
                commandTimeout: CommandTimeoutSeconds);

            runId = await connection.ExecuteScalarAsync<long>(
                "SELECT LAST_INSERT_ID();",
                commandTimeout: CommandTimeoutSeconds);

            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted);

            try
            {
                stage = "pairing reversals";
                var reversalCount = await BuildReversalPairsAsync(
                    connection,
                    transaction,
                    runId,
                    cutoffs.BoCutoffId);

                stage = "building primary matches";
                await BuildPrimaryMatchesAsync(
                    connection,
                    transaction,
                    cutoffs.CbsCutoffId,
                    cutoffs.BoCutoffId);

                stage = "building secondary matches";
                await BuildSecondaryMatchesAsync(
                    connection,
                    transaction,
                    cutoffs.CbsCutoffId,
                    cutoffs.BoCutoffId);

                var matchedAt = DateTime.UtcNow;

                stage = "persisting ID-based matches";
                await connection.ExecuteAsync(
                    """
                    INSERT INTO issuing_reconciliation_match
                    (
                        run_id, cbs_transaction_id, bo_transaction_id,
                        reconciliation_currency, transaction_category,
                        match_rule, rule_version, matched_at, match_status
                    )
                    SELECT
                        @RunId, t.cbs_id, t.bo_id,
                        t.reconciliation_currency, t.transaction_category,
                        t.match_rule, @RuleVersion, @MatchedAt, 'ACTIVE'
                    FROM tmp_issuing_match AS t;
                    """,
                    new
                    {
                        RunId = runId,
                        RuleVersion = IssuingTransactionClassification.RuleVersion,
                        MatchedAt = matchedAt
                    },
                    transaction,
                    CommandTimeoutSeconds);

                await connection.ExecuteAsync(
                    """
                    UPDATE issuing_cbs_transactions AS c
                    INNER JOIN tmp_issuing_match AS t ON t.cbs_id = c.id
                    SET c.reconciliation_status = 'MATCHED',
                        c.last_attempted_at = @MatchedAt,
                        c.last_reconciliation_run_id = @RunId,
                        c.matched_at = @MatchedAt,
                        c.match_rule = t.match_rule;

                    UPDATE issuing_bo_transaction AS b
                    INNER JOIN tmp_issuing_match AS t ON t.bo_id = b.id
                    SET b.reconciliation_status = 'MATCHED',
                        b.last_attempted_at = @MatchedAt,
                        b.last_reconciliation_run_id = @RunId,
                        b.matched_at = @MatchedAt,
                        b.match_rule = t.match_rule;
                    """,
                    new { RunId = runId, MatchedAt = matchedAt },
                    transaction,
                    CommandTimeoutSeconds);

                stage = "writing run results";
                await WriteRunResultsAsync(
                    connection,
                    transaction,
                    runId,
                    cutoffs,
                    matchedAt);

                stage = "updating unmatched source state";
                await MarkPendingRowsUnmatchedAsync(
                    connection,
                    transaction,
                    runId,
                    cutoffs,
                    matchedAt);

                RunCounts counts;
                using (var multi = await connection.QueryMultipleAsync(
                    """
                    SELECT
                        COALESCE(SUM(match_rule = 'PRIMARY'), 0)
                            AS PrimaryMatchCount,
                        COALESCE(SUM(match_rule = 'SECONDARY'), 0)
                            AS SecondaryMatchCount
                    FROM issuing_reconciliation_match
                    WHERE run_id = @RunId;

                    SELECT
                        COALESCE(SUM(result_status = 'MISSING_IN_CBS'), 0)
                            AS MissingInCbsCount,
                        COALESCE(SUM(result_status = 'MISSING_IN_BO'), 0)
                            AS MissingInBoCount
                    FROM issuing_reconciliation_run_result
                    WHERE run_id = @RunId;
                    """,
                    new { RunId = runId },
                    transaction,
                    CommandTimeoutSeconds))
                {
                    var matchCounts = await multi.ReadSingleAsync<MatchCounts>();
                    var missingCounts = await multi.ReadSingleAsync<MissingCounts>();
                    counts = new RunCounts
                    {
                        PrimaryMatchCount = matchCounts.PrimaryMatchCount,
                        SecondaryMatchCount = matchCounts.SecondaryMatchCount,
                        MissingInCbsCount = missingCounts.MissingInCbsCount,
                        MissingInBoCount = missingCounts.MissingInBoCount
                    };
                }

                var completedAt = DateTime.UtcNow;
                stage = "completing the reconciliation run";
                await connection.ExecuteAsync(
                    """
                    UPDATE issuing_reconciliation_run
                    SET completed_at = @CompletedAt,
                        status = 'COMPLETED',
                        primary_match_count = @PrimaryMatchCount,
                        secondary_match_count = @SecondaryMatchCount,
                        missing_in_cbs_count = @MissingInCbsCount,
                        missing_in_bo_count = @MissingInBoCount,
                        reversal_count = @ReversalCount,
                        error_message = NULL
                    WHERE id = @RunId;
                    """,
                    new
                    {
                        RunId = runId,
                        CompletedAt = completedAt,
                        counts.PrimaryMatchCount,
                        counts.SecondaryMatchCount,
                        counts.MissingInCbsCount,
                        counts.MissingInBoCount,
                        ReversalCount = reversalCount
                    },
                    transaction,
                    CommandTimeoutSeconds);

                await transaction.CommitAsync();

                return new ReconciliationResultResponse
                {
                    RunId = runId,
                    StartedAt = startedAt,
                    CompletedAt = completedAt,
                    MatchedCount = counts.PrimaryMatchCount
                        + counts.SecondaryMatchCount,
                    PrimaryMatchCount = counts.PrimaryMatchCount,
                    SecondaryMatchCount = counts.SecondaryMatchCount,
                    MissingInCbsCount = counts.MissingInCbsCount,
                    MissingInBoCount = counts.MissingInBoCount,
                    ReverseCount = reversalCount,
                    ReverseTransactionsArchived = reversalCount
                };
            }
            catch
            {
                try { await transaction.RollbackAsync(); } catch { }
                throw;
            }
        }
        catch (Exception ex)
        {
            if (runId > 0)
                await MarkRunFailedAsync(runId, stage, ex.Message);

            throw new InvalidOperationException(
                runId > 0
                    ? $"Reconciliation run {runId} failed during {stage}. " +
                      $"The transaction was rolled back. Error: {ex.Message}"
                    : $"Reconciliation failed during {stage}. Error: {ex.Message}",
                ex);
        }
        finally
        {
            try
            {
                if (connection.State == ConnectionState.Open)
                {
                    await connection.ExecuteAsync(
                        "SELECT RELEASE_LOCK(@LockName);",
                        new { LockName },
                        commandTimeout: CommandTimeoutSeconds);
                }
            }
            catch
            {
                // Closing the connection also releases the named lock.
            }
        }
    }

    private static async Task<CutoffRow> GetCutoffsAsync(MySqlConnection connection)
    {
        using var multi = await connection.QueryMultipleAsync(
            """
            SELECT COALESCE(MAX(c.id), 0)
            FROM issuing_cbs_transactions AS c
            LEFT JOIN issuing_upload_batch AS u ON u.id = c.upload_batch_id
            WHERE c.upload_batch_id IS NULL OR u.status = 'COMPLETED';

            SELECT COALESCE(MAX(b.id), 0)
            FROM issuing_bo_transaction AS b
            LEFT JOIN issuing_upload_batch AS u ON u.id = b.upload_batch_id
            WHERE b.upload_batch_id IS NULL OR u.status = 'COMPLETED';
            """,
            commandTimeout: CommandTimeoutSeconds);

        return new CutoffRow
        {
            CbsCutoffId = await multi.ReadSingleAsync<long>(),
            BoCutoffId = await multi.ReadSingleAsync<long>()
        };
    }

    private static async Task<int> BuildReversalPairsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long runId,
        long boCutoffId)
    {
        await connection.ExecuteAsync(
            """
            DROP TEMPORARY TABLE IF EXISTS tmp_issuing_reversal;
            CREATE TEMPORARY TABLE tmp_issuing_reversal
            (
                original_id BIGINT NOT NULL PRIMARY KEY,
                reversal_id BIGINT NOT NULL,
                utrnno VARCHAR(100) NOT NULL,
                auth_code VARCHAR(100) NOT NULL,
                original_amount DECIMAL(18,2),
                reversal_amount DECIMAL(18,2),
                UNIQUE KEY ux_tmp_reversal (reversal_id)
            ) ENGINE = InnoDB;
            """,
            transaction: transaction,
            commandTimeout: CommandTimeoutSeconds);

        await connection.ExecuteAsync(
            """
            INSERT INTO tmp_issuing_reversal
            (
                original_id, reversal_id, utrnno, auth_code,
                original_amount, reversal_amount
            )
            WITH originals AS
            (
                SELECT
                    b.id, TRIM(b.utrnno) AS utrnno,
                    TRIM(b.auth_code) AS auth_code, b.sttl_amount,
                    b.reconciliation_status,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY UPPER(TRIM(b.utrnno)),
                                     UPPER(TRIM(b.auth_code))
                        ORDER BY b.id
                    ) AS pair_number
                FROM issuing_bo_transaction AS b
                LEFT JOIN issuing_upload_batch AS u ON u.id = b.upload_batch_id
                WHERE b.id <= @BoCutoffId
                  AND b.reconciliation_status IN ('PENDING', 'UNMATCHED')
                  AND b.reversal_flag = 0
                  AND NULLIF(TRIM(b.utrnno), '') IS NOT NULL
                  AND NULLIF(TRIM(b.auth_code), '') IS NOT NULL
                  AND (b.upload_batch_id IS NULL OR u.status = 'COMPLETED')
            ),
            reversals AS
            (
                SELECT
                    b.id, TRIM(b.utrnno) AS utrnno,
                    TRIM(b.auth_code) AS auth_code, b.sttl_amount,
                    b.reconciliation_status,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY UPPER(TRIM(b.utrnno)),
                                     UPPER(TRIM(b.auth_code))
                        ORDER BY b.id
                    ) AS pair_number
                FROM issuing_bo_transaction AS b
                LEFT JOIN issuing_upload_batch AS u ON u.id = b.upload_batch_id
                WHERE b.id <= @BoCutoffId
                  AND b.reconciliation_status IN ('PENDING', 'UNMATCHED')
                  AND b.reversal_flag = 1
                  AND NULLIF(TRIM(b.utrnno), '') IS NOT NULL
                  AND NULLIF(TRIM(b.auth_code), '') IS NOT NULL
                  AND (b.upload_batch_id IS NULL OR u.status = 'COMPLETED')
            )
            SELECT
                o.id, r.id, o.utrnno, o.auth_code,
                o.sttl_amount, r.sttl_amount
            FROM originals AS o
            INNER JOIN reversals AS r
                ON UPPER(r.utrnno) = UPPER(o.utrnno)
               AND UPPER(r.auth_code) = UPPER(o.auth_code)
               AND r.pair_number = o.pair_number
            WHERE o.reconciliation_status = 'PENDING'
               OR r.reconciliation_status = 'PENDING';
            """,
            new { BoCutoffId = boCutoffId },
            transaction,
            CommandTimeoutSeconds);

        var reversalCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM tmp_issuing_reversal;",
            transaction: transaction,
            commandTimeout: CommandTimeoutSeconds);

        await connection.ExecuteAsync(
            """
            INSERT INTO issuing_reversal_transaction
            (
                run_id, original_bo_transaction_id,
                reversal_bo_transaction_id, utrnno, auth_code,
                original_sttl_amount, reversal_sttl_amount
            )
            SELECT
                @RunId, original_id, reversal_id, utrnno, auth_code,
                original_amount, reversal_amount
            FROM tmp_issuing_reversal;

            UPDATE issuing_bo_transaction AS b
            INNER JOIN tmp_issuing_reversal AS r ON r.original_id = b.id
            SET b.reconciliation_status = 'REVERSED',
                b.last_attempted_at = @PairedAt,
                b.last_reconciliation_run_id = @RunId,
                b.matched_at = NULL,
                b.match_rule = NULL;

            UPDATE issuing_bo_transaction AS b
            INNER JOIN tmp_issuing_reversal AS r ON r.reversal_id = b.id
            SET b.reconciliation_status = 'REVERSED',
                b.last_attempted_at = @PairedAt,
                b.last_reconciliation_run_id = @RunId,
                b.matched_at = NULL,
                b.match_rule = NULL;
            """,
            new { RunId = runId, PairedAt = DateTime.UtcNow },
            transaction,
            CommandTimeoutSeconds);

        return reversalCount;
    }

    private static async Task BuildPrimaryMatchesAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long cbsCutoffId,
        long boCutoffId)
    {
        await connection.ExecuteAsync(
            """
            DROP TEMPORARY TABLE IF EXISTS tmp_primary_cbs;
            DROP TEMPORARY TABLE IF EXISTS tmp_primary_bo;
            DROP TEMPORARY TABLE IF EXISTS tmp_issuing_match;

            CREATE TEMPORARY TABLE tmp_primary_cbs
            (
                transaction_id BIGINT NOT NULL PRIMARY KEY,
                match_key BINARY(32) NOT NULL,
                match_sequence BIGINT NOT NULL,
                KEY ix_tmp_primary_cbs (match_key, match_sequence)
            ) ENGINE = InnoDB;

            CREATE TEMPORARY TABLE tmp_primary_bo
            (
                transaction_id BIGINT NOT NULL PRIMARY KEY,
                match_key BINARY(32) NOT NULL,
                match_sequence BIGINT NOT NULL,
                KEY ix_tmp_primary_bo (match_key, match_sequence)
            ) ENGINE = InnoDB;

            CREATE TEMPORARY TABLE tmp_issuing_match
            (
                cbs_id BIGINT NOT NULL PRIMARY KEY,
                bo_id BIGINT NOT NULL,
                reconciliation_currency CHAR(3) NOT NULL,
                transaction_category VARCHAR(20) NOT NULL,
                match_rule VARCHAR(20) NOT NULL,
                UNIQUE KEY ux_tmp_match_bo (bo_id)
            ) ENGINE = InnoDB;
            """,
            transaction: transaction,
            commandTimeout: CommandTimeoutSeconds);

        await connection.ExecuteAsync(
            """
            INSERT INTO tmp_primary_cbs
                (transaction_id, match_key, match_sequence)
            SELECT
                c.id, c.primary_match_key,
                ROW_NUMBER() OVER
                    (PARTITION BY c.primary_match_key ORDER BY c.id)
            FROM issuing_cbs_transactions AS c
            LEFT JOIN issuing_upload_batch AS u ON u.id = c.upload_batch_id
            WHERE c.id <= @CbsCutoffId
              AND c.primary_match_key IS NOT NULL
              AND c.reconciliation_status IN ('PENDING', 'UNMATCHED')
              AND (c.upload_batch_id IS NULL OR u.status = 'COMPLETED')
              AND
              (
                  c.reconciliation_status = 'PENDING'
                  OR EXISTS
                  (
                      SELECT 1
                      FROM issuing_bo_transaction AS pending_bo
                      LEFT JOIN issuing_upload_batch AS pending_upload
                          ON pending_upload.id = pending_bo.upload_batch_id
                      WHERE pending_bo.id <= @BoCutoffId
                        AND pending_bo.reconciliation_status = 'PENDING'
                        AND pending_bo.primary_match_key = c.primary_match_key
                        AND (pending_bo.upload_batch_id IS NULL
                             OR pending_upload.status = 'COMPLETED')
                  )
              );

            INSERT INTO tmp_primary_bo
                (transaction_id, match_key, match_sequence)
            SELECT
                b.id, b.primary_match_key,
                ROW_NUMBER() OVER
                    (PARTITION BY b.primary_match_key ORDER BY b.id)
            FROM issuing_bo_transaction AS b
            LEFT JOIN issuing_upload_batch AS u ON u.id = b.upload_batch_id
            WHERE b.id <= @BoCutoffId
              AND b.primary_match_key IS NOT NULL
              AND b.reconciliation_status IN ('PENDING', 'UNMATCHED')
              AND (b.upload_batch_id IS NULL OR u.status = 'COMPLETED')
              AND
              (
                  b.reconciliation_status = 'PENDING'
                  OR EXISTS
                  (
                      SELECT 1
                      FROM issuing_cbs_transactions AS pending_cbs
                      LEFT JOIN issuing_upload_batch AS pending_upload
                          ON pending_upload.id = pending_cbs.upload_batch_id
                      WHERE pending_cbs.id <= @CbsCutoffId
                        AND pending_cbs.reconciliation_status = 'PENDING'
                        AND pending_cbs.primary_match_key = b.primary_match_key
                        AND (pending_cbs.upload_batch_id IS NULL
                             OR pending_upload.status = 'COMPLETED')
                  )
              );
            """,
            new { CbsCutoffId = cbsCutoffId, BoCutoffId = boCutoffId },
            transaction,
            CommandTimeoutSeconds);

        await connection.ExecuteAsync(
            """
            INSERT INTO tmp_issuing_match
            (
                cbs_id, bo_id, reconciliation_currency,
                transaction_category, match_rule
            )
            SELECT
                pc.transaction_id, pb.transaction_id,
                c.reconciliation_currency, c.transaction_category,
                'PRIMARY'
            FROM tmp_primary_cbs AS pc
            INNER JOIN tmp_primary_bo AS pb
                ON pb.match_key = pc.match_key
               AND pb.match_sequence = pc.match_sequence
            INNER JOIN issuing_cbs_transactions AS c
                ON c.id = pc.transaction_id
            INNER JOIN issuing_bo_transaction AS b
                ON b.id = pb.transaction_id
            WHERE c.reconciliation_currency = b.reconciliation_currency
              AND c.transaction_category = b.transaction_category
              AND c.amount = b.sttl_amount
              AND UPPER(TRIM(c.unique_reference_no)) = UPPER(TRIM(b.utrnno))
              AND UPPER(TRIM(c.rrn)) = UPPER(TRIM(b.rrn))
              AND UPPER(TRIM(c.auth_code)) = UPPER(TRIM(b.auth_code));
            """,
            transaction: transaction,
            commandTimeout: CommandTimeoutSeconds);
    }

    private static async Task BuildSecondaryMatchesAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long cbsCutoffId,
        long boCutoffId)
    {
        await connection.ExecuteAsync(
            """
            DROP TEMPORARY TABLE IF EXISTS tmp_secondary_cbs;
            DROP TEMPORARY TABLE IF EXISTS tmp_secondary_bo;

            CREATE TEMPORARY TABLE tmp_secondary_cbs
            (
                transaction_id BIGINT NOT NULL PRIMARY KEY,
                match_key BINARY(32) NOT NULL,
                match_sequence BIGINT NOT NULL,
                KEY ix_tmp_secondary_cbs (match_key, match_sequence)
            ) ENGINE = InnoDB;

            CREATE TEMPORARY TABLE tmp_secondary_bo
            (
                transaction_id BIGINT NOT NULL PRIMARY KEY,
                match_key BINARY(32) NOT NULL,
                match_sequence BIGINT NOT NULL,
                KEY ix_tmp_secondary_bo (match_key, match_sequence)
            ) ENGINE = InnoDB;
            """,
            transaction: transaction,
            commandTimeout: CommandTimeoutSeconds);

        await connection.ExecuteAsync(
            """
            INSERT INTO tmp_secondary_cbs
                (transaction_id, match_key, match_sequence)
            SELECT
                c.id, c.secondary_match_key,
                ROW_NUMBER() OVER
                    (PARTITION BY c.secondary_match_key ORDER BY c.id)
            FROM issuing_cbs_transactions AS c
            LEFT JOIN issuing_upload_batch AS u ON u.id = c.upload_batch_id
            WHERE c.id <= @CbsCutoffId
              AND c.secondary_match_key IS NOT NULL
              AND c.reconciliation_status IN ('PENDING', 'UNMATCHED')
              AND NOT EXISTS
                  (SELECT 1 FROM tmp_issuing_match AS m WHERE m.cbs_id = c.id)
              AND (c.upload_batch_id IS NULL OR u.status = 'COMPLETED')
              AND
              (
                  c.reconciliation_status = 'PENDING'
                  OR EXISTS
                  (
                      SELECT 1
                      FROM issuing_bo_transaction AS pending_bo
                      LEFT JOIN issuing_upload_batch AS pending_upload
                          ON pending_upload.id = pending_bo.upload_batch_id
                      WHERE pending_bo.id <= @BoCutoffId
                        AND pending_bo.reconciliation_status = 'PENDING'
                        AND pending_bo.secondary_match_key = c.secondary_match_key
                        AND (pending_bo.upload_batch_id IS NULL
                             OR pending_upload.status = 'COMPLETED')
                  )
              );

            INSERT INTO tmp_secondary_bo
                (transaction_id, match_key, match_sequence)
            SELECT
                b.id, b.secondary_match_key,
                ROW_NUMBER() OVER
                    (PARTITION BY b.secondary_match_key ORDER BY b.id)
            FROM issuing_bo_transaction AS b
            LEFT JOIN issuing_upload_batch AS u ON u.id = b.upload_batch_id
            WHERE b.id <= @BoCutoffId
              AND b.secondary_match_key IS NOT NULL
              AND b.reconciliation_status IN ('PENDING', 'UNMATCHED')
              AND NOT EXISTS
                  (SELECT 1 FROM tmp_issuing_match AS m WHERE m.bo_id = b.id)
              AND (b.upload_batch_id IS NULL OR u.status = 'COMPLETED')
              AND
              (
                  b.reconciliation_status = 'PENDING'
                  OR EXISTS
                  (
                      SELECT 1
                      FROM issuing_cbs_transactions AS pending_cbs
                      LEFT JOIN issuing_upload_batch AS pending_upload
                          ON pending_upload.id = pending_cbs.upload_batch_id
                      WHERE pending_cbs.id <= @CbsCutoffId
                        AND pending_cbs.reconciliation_status = 'PENDING'
                        AND pending_cbs.secondary_match_key = b.secondary_match_key
                        AND (pending_cbs.upload_batch_id IS NULL
                             OR pending_upload.status = 'COMPLETED')
                  )
              );
            """,
            new { CbsCutoffId = cbsCutoffId, BoCutoffId = boCutoffId },
            transaction,
            CommandTimeoutSeconds);

        await connection.ExecuteAsync(
            """
            INSERT INTO tmp_issuing_match
            (
                cbs_id, bo_id, reconciliation_currency,
                transaction_category, match_rule
            )
            SELECT
                sc.transaction_id, sb.transaction_id,
                c.reconciliation_currency, c.transaction_category,
                'SECONDARY'
            FROM tmp_secondary_cbs AS sc
            INNER JOIN tmp_secondary_bo AS sb
                ON sb.match_key = sc.match_key
               AND sb.match_sequence = sc.match_sequence
            INNER JOIN issuing_cbs_transactions AS c
                ON c.id = sc.transaction_id
            INNER JOIN issuing_bo_transaction AS b
                ON b.id = sb.transaction_id
            WHERE c.reconciliation_currency = b.reconciliation_currency
              AND c.transaction_category = b.transaction_category
              AND c.amount = b.sttl_amount
              AND UPPER(TRIM(c.auth_code)) = UPPER(TRIM(b.auth_code));
            """,
            transaction: transaction,
            commandTimeout: CommandTimeoutSeconds);
    }

    private static Task WriteRunResultsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long runId,
        CutoffRow cutoffs,
        DateTime createdAt) => connection.ExecuteAsync(
            """
            INSERT INTO issuing_reconciliation_run_result
            (
                run_id, result_status, cbs_transaction_id,
                bo_transaction_id, match_id,
                reconciliation_currency, transaction_category,
                business_date, created_at
            )
            SELECT
                @RunId, 'MATCHED', m.cbs_transaction_id,
                m.bo_transaction_id, m.id,
                m.reconciliation_currency, m.transaction_category,
                COALESCE(DATE(c.posting_date), DATE(b.trans_date),
                         DATE(b.transaction_date), DATE(b.ep_sttl_date),
                         DATE(b.run_date)),
                @CreatedAt
            FROM issuing_reconciliation_match AS m
            INNER JOIN issuing_cbs_transactions AS c
                ON c.id = m.cbs_transaction_id
            INNER JOIN issuing_bo_transaction AS b
                ON b.id = m.bo_transaction_id
            WHERE m.run_id = @RunId;

            INSERT INTO issuing_reconciliation_run_result
            (
                run_id, result_status, cbs_transaction_id,
                reconciliation_currency, transaction_category,
                business_date, created_at
            )
            SELECT
                @RunId, 'MISSING_IN_BO', c.id,
                c.reconciliation_currency, c.transaction_category,
                DATE(c.posting_date), @CreatedAt
            FROM issuing_cbs_transactions AS c
            LEFT JOIN issuing_upload_batch AS u ON u.id = c.upload_batch_id
            WHERE c.id <= @CbsCutoffId
              AND c.reconciliation_status = 'PENDING'
              AND (c.upload_batch_id IS NULL OR u.status = 'COMPLETED');

            INSERT INTO issuing_reconciliation_run_result
            (
                run_id, result_status, bo_transaction_id,
                reconciliation_currency, transaction_category,
                business_date, created_at
            )
            SELECT
                @RunId, 'MISSING_IN_CBS', b.id,
                b.reconciliation_currency, b.transaction_category,
                COALESCE(DATE(b.trans_date), DATE(b.transaction_date),
                         DATE(b.ep_sttl_date), DATE(b.run_date)),
                @CreatedAt
            FROM issuing_bo_transaction AS b
            LEFT JOIN issuing_upload_batch AS u ON u.id = b.upload_batch_id
            WHERE b.id <= @BoCutoffId
              AND b.reconciliation_status = 'PENDING'
              AND (b.upload_batch_id IS NULL OR u.status = 'COMPLETED');
            """,
            new
            {
                RunId = runId,
                cutoffs.CbsCutoffId,
                cutoffs.BoCutoffId,
                CreatedAt = createdAt
            },
            transaction,
            CommandTimeoutSeconds);

    private static Task MarkPendingRowsUnmatchedAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long runId,
        CutoffRow cutoffs,
        DateTime attemptedAt) => connection.ExecuteAsync(
            """
            UPDATE issuing_cbs_transactions AS c
            LEFT JOIN issuing_upload_batch AS u ON u.id = c.upload_batch_id
            SET c.reconciliation_status = 'UNMATCHED',
                c.last_attempted_at = @AttemptedAt,
                c.last_reconciliation_run_id = @RunId
            WHERE c.id <= @CbsCutoffId
              AND c.reconciliation_status = 'PENDING'
              AND (c.upload_batch_id IS NULL OR u.status = 'COMPLETED');

            UPDATE issuing_bo_transaction AS b
            LEFT JOIN issuing_upload_batch AS u ON u.id = b.upload_batch_id
            SET b.reconciliation_status = 'UNMATCHED',
                b.last_attempted_at = @AttemptedAt,
                b.last_reconciliation_run_id = @RunId
            WHERE b.id <= @BoCutoffId
              AND b.reconciliation_status = 'PENDING'
              AND (b.upload_batch_id IS NULL OR u.status = 'COMPLETED');
            """,
            new
            {
                RunId = runId,
                cutoffs.CbsCutoffId,
                cutoffs.BoCutoffId,
                AttemptedAt = attemptedAt
            },
            transaction,
            CommandTimeoutSeconds);

    private async Task MarkRunFailedAsync(long runId, string stage, string error)
    {
        try
        {
            await using var connection =
                (MySqlConnection)_connectionFactory.CreateConnection();
            await connection.OpenAsync();
            var message = $"Stage: {stage}. {error}";

            await connection.ExecuteAsync(
                """
                UPDATE issuing_reconciliation_run
                SET status = 'FAILED', completed_at = @CompletedAt,
                    error_message = @ErrorMessage
                WHERE id = @RunId;
                """,
                new
                {
                    RunId = runId,
                    CompletedAt = DateTime.UtcNow,
                    ErrorMessage = message.Length <= 4000
                        ? message
                        : message[..4000]
                },
                commandTimeout: CommandTimeoutSeconds);
        }
        catch
        {
            // The database may still be unavailable after a server failure.
        }
    }

    public async Task<PagedResponse<ReconciliationStoredResultResponse>>
        GetResultsAsync(ReconciliationResultsRequest request)
    {
        if (request.RunId < 0)
            throw new InvalidDataException("RunId cannot be negative.");

        var status = NormalizeResultStatus(request.ReconciliationStatus);
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 500);
        var offset = (page - 1) * pageSize;

        await using var connection =
            (MySqlConnection)_connectionFactory.CreateConnection();
        await connection.OpenAsync();

        var runId = await connection.QueryFirstOrDefaultAsync<long?>(
            """
            SELECT id FROM issuing_reconciliation_run
            WHERE status = 'COMPLETED'
              AND (@RequestedRunId = 0 OR id = @RequestedRunId)
            ORDER BY id DESC LIMIT 1;
            """,
            new { RequestedRunId = request.RunId },
            commandTimeout: CommandTimeoutSeconds);
        if (!runId.HasValue)
            throw new InvalidDataException(request.RunId == 0
                ? "No completed reconciliation run was found."
                : $"Completed reconciliation run {request.RunId} was not found.");

        const string sql = """
            SELECT
                rr.id AS Id, rr.run_id AS RunId,
                rr.result_status AS ReconciliationStatus,
                rr.business_date AS BusinessDate,
                rr.created_at AS CreatedAt,
                rr.reconciliation_currency AS ReconciliationCurrency,
                rr.transaction_category AS TransactionCategory,
                m.match_rule AS MatchRule, m.matched_at AS MatchedAt,
                IF(c.id IS NULL, NULL, JSON_OBJECT(
                    'Id', c.id, 'AccountNo', c.account_no,
                    'PostingDate', c.posting_date, 'ValueDate', c.value_date,
                    'BatchId', c.batch_id, 'PostingBranch', c.posting_branch,
                    'UniqueReferenceNo', c.unique_reference_no,
                    'DebitCredit', c.debit_credit, 'Amount', c.amount,
                    'TransactionCode', c.transaction_code,
                    'TransactionName', c.transaction_name,
                    'Currency', c.currency, 'TimeStamp', c.time_stamp,
                    'UniqueId', c.unique_id, 'Narrative1', c.narrative_1,
                    'Narrative2', c.narrative_2, 'Narrative3', c.narrative_3,
                    'Narrative4', c.narrative_4, 'RRN', c.rrn,
                    'AuthCode', c.auth_code, 'UploadedAt', c.uploaded_at
                )) AS CbsDataJson,
                IF(b.id IS NULL, NULL, JSON_OBJECT(
                    'Id', b.id, 'SESSION_ID', b.session_id,
                    'BO_OPER_ID', b.bo_oper_id, 'EP_STTL_DATE', b.ep_sttl_date,
                    'RUN_DATE', b.run_date, 'TRX_TYPE', b.trx_type,
                    'MESSAGE_TYPE', b.message_type,
                    'CONTRACT_TYPE', b.contract_type,
                    'CARD_NUMBER', b.card_number,
                    'ACCOUNT_NUMBER', b.account_number,
                    'SENDER_ACCOUNT_NUMBER', b.sender_account_number,
                    'AUTH_CODE', b.auth_code, 'ARN', b.arn,
                    'TRANS_DATE', b.trans_date, 'TXN_CURRENCY', b.txn_currency,
                    'STTL_AMOUNT', b.sttl_amount, 'ST_REV', b.st_rev,
                    'MERCHANT_NAME', b.merchant_name,
                    'MERCHANT_COUNTRY', b.merchant_country,
                    'TRANSACTION_DATE', b.transaction_date,
                    'REVERSAL_FLAG', b.reversal_flag,
                    'AUTH_MESSAGE_TYPE', b.auth_message_type,
                    'UTRNNO', b.utrnno, 'RRN', b.rrn,
                    'UploadedAt', b.uploaded_at
                )) AS BoDataJson
            FROM issuing_reconciliation_run_result AS rr
            LEFT JOIN issuing_reconciliation_match AS m ON m.id = rr.match_id
            LEFT JOIN issuing_cbs_transactions AS c ON c.id = rr.cbs_transaction_id
            LEFT JOIN issuing_bo_transaction AS b ON b.id = rr.bo_transaction_id
            WHERE rr.run_id = @RunId
              AND (@Status IS NULL OR rr.result_status = @Status)
            ORDER BY rr.id
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*)
            FROM issuing_reconciliation_run_result
            WHERE run_id = @RunId
              AND (@Status IS NULL OR result_status = @Status);
            """;

        using var multi = await connection.QueryMultipleAsync(
            sql,
            new { RunId = runId.Value, Status = status, PageSize = pageSize, Offset = offset },
            commandTimeout: CommandTimeoutSeconds);
        var rows = (await multi.ReadAsync<StoredResultDbRow>()).ToList();
        var total = await multi.ReadSingleAsync<int>();

        return new PagedResponse<ReconciliationStoredResultResponse>
        {
            Items = rows.Select(MapStoredResult).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = total == 0 ? 0 : (int)Math.Ceiling((double)total / pageSize)
        };
    }

    public async Task<PagedResponse<ReconciliationStoredResultResponse>>
        GetDailyMatchesAsync(DailyMatchesRequest request)
    {
        var reconciliationDate =
            (request.ReconciliationDate ?? DateTime.Today).Date;
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 500);
        var offset = (page - 1) * pageSize;

        await using var connection =
            (MySqlConnection)_connectionFactory.CreateConnection();
        await connection.OpenAsync();

        const string sql = """
            SELECT
                rr.id AS Id, rr.run_id AS RunId,
                rr.result_status AS ReconciliationStatus,
                rr.business_date AS BusinessDate,
                rr.created_at AS CreatedAt,
                rr.reconciliation_currency AS ReconciliationCurrency,
                rr.transaction_category AS TransactionCategory,
                m.match_rule AS MatchRule, m.matched_at AS MatchedAt,
                JSON_OBJECT(
                    'Id', c.id, 'AccountNo', c.account_no,
                    'PostingDate', c.posting_date, 'ValueDate', c.value_date,
                    'BatchId', c.batch_id, 'PostingBranch', c.posting_branch,
                    'UniqueReferenceNo', c.unique_reference_no,
                    'DebitCredit', c.debit_credit, 'Amount', c.amount,
                    'TransactionCode', c.transaction_code,
                    'TransactionName', c.transaction_name,
                    'Currency', c.currency, 'TimeStamp', c.time_stamp,
                    'UniqueId', c.unique_id, 'Narrative1', c.narrative_1,
                    'Narrative2', c.narrative_2, 'Narrative3', c.narrative_3,
                    'Narrative4', c.narrative_4, 'RRN', c.rrn,
                    'AuthCode', c.auth_code, 'UploadedAt', c.uploaded_at
                ) AS CbsDataJson,
                JSON_OBJECT(
                    'Id', b.id, 'SESSION_ID', b.session_id,
                    'BO_OPER_ID', b.bo_oper_id, 'EP_STTL_DATE', b.ep_sttl_date,
                    'RUN_DATE', b.run_date, 'TRX_TYPE', b.trx_type,
                    'MESSAGE_TYPE', b.message_type,
                    'CONTRACT_TYPE', b.contract_type,
                    'CARD_NUMBER', b.card_number,
                    'ACCOUNT_NUMBER', b.account_number,
                    'SENDER_ACCOUNT_NUMBER', b.sender_account_number,
                    'AUTH_CODE', b.auth_code, 'ARN', b.arn,
                    'TRANS_DATE', b.trans_date, 'TXN_CURRENCY', b.txn_currency,
                    'STTL_AMOUNT', b.sttl_amount, 'ST_REV', b.st_rev,
                    'MERCHANT_NAME', b.merchant_name,
                    'MERCHANT_COUNTRY', b.merchant_country,
                    'TRANSACTION_DATE', b.transaction_date,
                    'REVERSAL_FLAG', b.reversal_flag,
                    'AUTH_MESSAGE_TYPE', b.auth_message_type,
                    'UTRNNO', b.utrnno, 'RRN', b.rrn,
                    'UploadedAt', b.uploaded_at
                ) AS BoDataJson
            FROM issuing_reconciliation_run_result AS rr
            INNER JOIN issuing_reconciliation_run AS r ON r.id = rr.run_id
            INNER JOIN issuing_reconciliation_match AS m
                ON m.id = rr.match_id AND m.match_status = 'ACTIVE'
            INNER JOIN issuing_cbs_transactions AS c
                ON c.id = rr.cbs_transaction_id
            INNER JOIN issuing_bo_transaction AS b
                ON b.id = rr.bo_transaction_id
            WHERE r.reconciliation_date = @ReconciliationDate
              AND r.status = 'COMPLETED'
              AND rr.result_status = 'MATCHED'
            ORDER BY rr.id
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*)
            FROM issuing_reconciliation_run_result AS rr
            INNER JOIN issuing_reconciliation_run AS r ON r.id = rr.run_id
            INNER JOIN issuing_reconciliation_match AS m
                ON m.id = rr.match_id AND m.match_status = 'ACTIVE'
            WHERE r.reconciliation_date = @ReconciliationDate
              AND r.status = 'COMPLETED'
              AND rr.result_status = 'MATCHED';
            """;

        using var multi = await connection.QueryMultipleAsync(
            sql,
            new { ReconciliationDate = reconciliationDate, PageSize = pageSize, Offset = offset },
            commandTimeout: CommandTimeoutSeconds);
        var rows = (await multi.ReadAsync<StoredResultDbRow>()).ToList();
        var total = await multi.ReadSingleAsync<int>();
        return new PagedResponse<ReconciliationStoredResultResponse>
        {
            Items = rows.Select(MapStoredResult).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = total == 0 ? 0 : (int)Math.Ceiling((double)total / pageSize)
        };
    }

    public async Task<MonthlyUnresolvedResponse> GetMonthlyUnresolvedAsync(
        MonthlyUnresolvedRequest request)
    {
        var status = NormalizeUnresolvedStatus(request.ReconciliationStatus);
        var ageBucket = NormalizeAgeBucket(request.AgeBucket);
        var asOfDate = (request.AsOfDate ?? DateTime.Today).Date;
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 500);
        var offset = (page - 1) * pageSize;

        await using var connection =
            (MySqlConnection)_connectionFactory.CreateConnection();
        await connection.OpenAsync();

        var latestRunId = await connection.QueryFirstOrDefaultAsync<long?>(
            """
            SELECT id FROM issuing_reconciliation_run
            WHERE status = 'COMPLETED'
              AND (@RunId = 0 OR id = @RunId)
            ORDER BY id DESC LIMIT 1;
            """,
            new { request.RunId },
            commandTimeout: CommandTimeoutSeconds);

        if (!latestRunId.HasValue)
            throw new InvalidDataException("No completed reconciliation run was found.");

        const string ageExpression = """
            CASE
                WHEN business_date IS NULL THEN 'UNKNOWN'
                WHEN business_date > DATE_SUB(@AsOfDate, INTERVAL 1 MONTH) THEN '<1 month'
                WHEN business_date > DATE_SUB(@AsOfDate, INTERVAL 3 MONTH) THEN '1-3 months'
                WHEN business_date > DATE_SUB(@AsOfDate, INTERVAL 6 MONTH) THEN '3-6 months'
                WHEN business_date > DATE_SUB(@AsOfDate, INTERVAL 12 MONTH) THEN '6-12 months'
                ELSE '>12 months'
            END
            """;

        var sourceCte = $"""
            WITH unresolved AS
            (
                SELECT
                    c.id, c.last_reconciliation_run_id AS run_id,
                    'MISSING_IN_BO' AS result_status,
                    DATE(c.posting_date) AS business_date,
                    c.last_attempted_at AS created_at,
                    JSON_OBJECT(
                        'Id', c.id, 'AccountNo', c.account_no,
                        'PostingDate', c.posting_date,
                        'UniqueReferenceNo', c.unique_reference_no,
                        'Amount', c.amount, 'RRN', c.rrn,
                        'AuthCode', c.auth_code
                    ) AS cbs_json,
                    NULL AS bo_json
                FROM issuing_cbs_transactions AS c
                WHERE c.reconciliation_status = 'UNMATCHED'
                  AND (@RunId = 0 OR c.last_reconciliation_run_id = @RunId)

                UNION ALL

                SELECT
                    b.id, b.last_reconciliation_run_id,
                    'MISSING_IN_CBS',
                    COALESCE(DATE(b.trans_date), DATE(b.transaction_date),
                             DATE(b.ep_sttl_date), DATE(b.run_date)),
                    b.last_attempted_at,
                    NULL,
                    JSON_OBJECT(
                        'Id', b.id, 'SESSION_ID', b.session_id,
                        'TRX_TYPE', b.trx_type, 'AUTH_CODE', b.auth_code,
                        'UTRNNO', b.utrnno, 'RRN', b.rrn,
                        'STTL_AMOUNT', b.sttl_amount,
                        'TXN_CURRENCY', b.txn_currency
                    )
                FROM issuing_bo_transaction AS b
                WHERE b.reconciliation_status = 'UNMATCHED'
                  AND (@RunId = 0 OR b.last_reconciliation_run_id = @RunId)
            ),
            bucketed AS
            (
                SELECT unresolved.*, {ageExpression} AS age_bucket
                FROM unresolved
            )
            """;

        var sql = $"""
            {sourceCte}
            SELECT
                id AS Id, run_id AS RunId,
                result_status AS ReconciliationStatus,
                business_date AS BusinessDate, age_bucket AS AgeBucket,
                cbs_json AS CbsDataJson, bo_json AS BoDataJson,
                created_at AS CreatedAt
            FROM bucketed
            WHERE (@Status IS NULL OR result_status = @Status)
              AND (@AgeBucket IS NULL OR age_bucket = @AgeBucket)
            ORDER BY business_date, id
            LIMIT @PageSize OFFSET @Offset;

            {sourceCte}
            SELECT COUNT(*)
            FROM bucketed
            WHERE (@Status IS NULL OR result_status = @Status)
              AND (@AgeBucket IS NULL OR age_bucket = @AgeBucket);

            {sourceCte}
            SELECT
                result_status AS ReconciliationStatus,
                age_bucket AS AgeBucket,
                COUNT(*) AS ItemCount
            FROM bucketed
            WHERE (@Status IS NULL OR result_status = @Status)
            GROUP BY result_status, age_bucket
            ORDER BY result_status, age_bucket;
            """;

        using var multi = await connection.QueryMultipleAsync(
            sql,
            new
            {
                RunId = request.RunId,
                AsOfDate = asOfDate,
                Status = status,
                AgeBucket = ageBucket,
                PageSize = pageSize,
                Offset = offset
            },
            commandTimeout: CommandTimeoutSeconds);

        var rows = (await multi.ReadAsync<StoredResultDbRow>()).ToList();
        var total = await multi.ReadSingleAsync<int>();
        var summary = (await multi.ReadAsync<AgeBucketSummaryResponse>()).ToList();

        return new MonthlyUnresolvedResponse
        {
            RunId = latestRunId.Value,
            AsOfDate = asOfDate,
            Summary = summary,
            Results = new PagedResponse<ReconciliationStoredResultResponse>
            {
                Items = rows.Select(MapStoredResult).ToList(),
                Page = page,
                PageSize = pageSize,
                TotalItems = total,
                TotalPages = total == 0 ? 0 : (int)Math.Ceiling((double)total / pageSize)
            }
        };
    }

    public async Task<PagedResponse<IssuingReversalResponse>> GetReversalsAsync(
        IssuingReversalRequest request)
    {
        if (request.RunId <= 0)
            throw new InvalidDataException("RunId must be greater than zero.");

        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 500);
        var offset = (page - 1) * pageSize;

        await using var connection =
            (MySqlConnection)_connectionFactory.CreateConnection();
        await connection.OpenAsync();

        using var multi = await connection.QueryMultipleAsync(
            """
            SELECT
                id AS Id, run_id AS RunId, utrnno AS Utrnno,
                auth_code AS AuthCode,
                original_bo_transaction_id AS OriginalBoTransactionId,
                reversal_bo_transaction_id AS ReversalBoTransactionId,
                original_sttl_amount AS OriginalSettlementAmount,
                reversal_sttl_amount AS ReversalSettlementAmount,
                paired_at AS CreatedAt
            FROM issuing_reversal_transaction
            WHERE run_id = @RunId
            ORDER BY id LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*) FROM issuing_reversal_transaction
            WHERE run_id = @RunId;
            """,
            new { request.RunId, PageSize = pageSize, Offset = offset },
            commandTimeout: CommandTimeoutSeconds);

        var items = (await multi.ReadAsync<IssuingReversalResponse>()).ToList();
        var total = await multi.ReadSingleAsync<int>();

        return new PagedResponse<IssuingReversalResponse>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = total == 0 ? 0 : (int)Math.Ceiling((double)total / pageSize)
        };
    }

    private static ReconciliationStoredResultResponse MapStoredResult(
        StoredResultDbRow row) => new()
    {
        Id = row.Id,
        RunId = row.RunId,
        ReconciliationStatus = row.ReconciliationStatus,
        BusinessDate = row.BusinessDate,
        AgeBucket = row.AgeBucket,
        ReconciliationCurrency = row.ReconciliationCurrency,
        TransactionCategory = row.TransactionCategory,
        MatchRule = row.MatchRule,
        MatchedAt = row.MatchedAt,
        CbsData = ParseJson(row.CbsDataJson),
        BoData = ParseJson(row.BoDataJson),
        CreatedAt = row.CreatedAt
    };

    private static JsonElement? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string? NormalizeResultStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return null;

        return status.Trim().ToUpperInvariant() switch
        {
            "MATCHED" => "MATCHED",
            "MISSING_IN_CBS" => "MISSING_IN_CBS",
            "MISSING_IN_BO" => "MISSING_IN_BO",
            _ => throw new InvalidDataException(
                $"Unsupported reconciliation status '{status}'.")
        };
    }

    private static string? NormalizeUnresolvedStatus(string? status)
    {
        var normalized = NormalizeResultStatus(status);
        return normalized is null or "MISSING_IN_CBS" or "MISSING_IN_BO"
            ? normalized
            : throw new InvalidDataException(
                "Unresolved status must be MISSING_IN_CBS or MISSING_IN_BO.");
    }

    private static string? NormalizeAgeBucket(string? ageBucket)
    {
        if (string.IsNullOrWhiteSpace(ageBucket))
            return null;

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

    private sealed class CutoffRow
    {
        public long CbsCutoffId { get; init; }
        public long BoCutoffId { get; init; }
    }

    private sealed class MatchCounts
    {
        public int PrimaryMatchCount { get; init; }
        public int SecondaryMatchCount { get; init; }
    }

    private sealed class MissingCounts
    {
        public int MissingInCbsCount { get; init; }
        public int MissingInBoCount { get; init; }
    }

    private sealed class RunCounts
    {
        public int PrimaryMatchCount { get; init; }
        public int SecondaryMatchCount { get; init; }
        public int MissingInCbsCount { get; init; }
        public int MissingInBoCount { get; init; }
    }

    private sealed class StoredResultDbRow
    {
        public long Id { get; init; }
        public long RunId { get; init; }
        public string ReconciliationStatus { get; init; } = string.Empty;
        public DateTime? BusinessDate { get; init; }
        public string? AgeBucket { get; init; }
        public string? ReconciliationCurrency { get; init; }
        public string? TransactionCategory { get; init; }
        public string? MatchRule { get; init; }
        public DateTime? MatchedAt { get; init; }
        public string? CbsDataJson { get; init; }
        public string? BoDataJson { get; init; }
        public DateTime CreatedAt { get; init; }
    }
}
