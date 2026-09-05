using Dapper;
using MySqlConnector;
using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.AcquiringTransaction;
using VISA_RECON.API.Application.Interfaces;
using VISA_RECON.API.Application.Interfaces.Repository;

namespace VISA_RECON.API.Infrastructure.Persistence;

public sealed class AcquiringReconciliationRepository : IAcquiringReconciliationRepository
{
    private const int CommandTimeoutSeconds = 720;
    private const string LockName = "visa_recon:acquiring_reconciliation";
    private readonly IDbConnectionFactory _factory;

    public AcquiringReconciliationRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<AcquiringReconciliationRunResponse> RunAsync()
    {
        await using var connection = (MySqlConnection)_factory.CreateConnection();
        await connection.OpenAsync();

        var lockAcquired = await connection.ExecuteScalarAsync<int>(
            "SELECT GET_LOCK(@LockName, 0);",
            new { LockName },
            commandTimeout: CommandTimeoutSeconds);

        if (lockAcquired != 1)
            throw new InvalidOperationException("Another acquiring reconciliation is already running.");

        long runId = 0;
        var startedAt = DateTime.UtcNow;

        try
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO acquiring_reconciliation_run (started_at, status)
                VALUES (@StartedAt, 'RUNNING');
                """,
                new { StartedAt = startedAt },
                commandTimeout: CommandTimeoutSeconds);

            runId = await connection.ExecuteScalarAsync<long>(
                "SELECT LAST_INSERT_ID();",
                commandTimeout: CommandTimeoutSeconds);

            await CreateTemporaryTablesAsync(connection);
            await using var transaction = await connection.BeginTransactionAsync(
                System.Data.IsolationLevel.RepeatableRead);

            try
            {
                await ArchiveReversalsAsync(connection, transaction, runId);
                await PopulateEpRrnsAsync(connection, transaction);
                await PopulateGlSideAsync(connection, transaction);
                await PopulateFeSideAsync(connection, transaction, runId);
                await InsertMatchedAsync(connection, transaction, runId);
                await InsertMissingInCbsAsync(connection, transaction, runId);
                await InsertMissingInBoAsync(connection, transaction, runId);

                var counts = await GetCountsAsync(connection, transaction, runId);
                var completedAt = DateTime.UtcNow;

                await connection.ExecuteAsync(
                    """
                    UPDATE acquiring_reconciliation_run
                    SET completed_at = @CompletedAt,
                        status = 'COMPLETED',
                        matched_count = @MatchedCount,
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
                        counts.MatchedCount,
                        counts.MissingInCbsCount,
                        counts.MissingInBoCount,
                        counts.ReversalCount
                    },
                    transaction,
                    CommandTimeoutSeconds);

                await transaction.CommitAsync();

                return new AcquiringReconciliationRunResponse
                {
                    RunId = runId,
                    StartedAt = startedAt,
                    CompletedAt = completedAt,
                    MatchedCount = counts.MatchedCount,
                    MissingInCbsCount = counts.MissingInCbsCount,
                    MissingInBoCount = counts.MissingInBoCount,
                    ReversalCount = counts.ReversalCount
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
                    // Preserve the reconciliation failure.
                }

                throw new InvalidOperationException(
                    $"Acquiring reconciliation run {runId} was rolled back. Error: {ex.Message}", ex);
            }
        }
        catch (Exception ex)
        {
            if (runId > 0)
            {
                try
                {
                    await MarkRunFailedAsync(connection, runId, ex.Message);
                }
                catch
                {
                    // Preserve the original reconciliation failure.
                }
            }

            throw;
        }
        finally
        {
            try
            {
                await connection.ExecuteAsync(
                    "SELECT RELEASE_LOCK(@LockName);",
                    new { LockName },
                    commandTimeout: CommandTimeoutSeconds);
            }
            catch
            {
                // Disposing the connection also releases the named lock.
            }
        }
    }

    private static Task CreateTemporaryTablesAsync(MySqlConnection connection) =>
        connection.ExecuteAsync(
            """
            CREATE TEMPORARY TABLE tmp_acq_ep_rrn
            (
                rrn VARCHAR(100) NOT NULL PRIMARY KEY,
                ep_id BIGINT NOT NULL
            ) ENGINE = InnoDB;

            CREATE TEMPORARY TABLE tmp_acq_gl_side
            (
                gl_id BIGINT NOT NULL,
                ep_id BIGINT NOT NULL,
                business_date DATE,
                rrn VARCHAR(100) NOT NULL,
                auth_code VARCHAR(100) NOT NULL,
                unique_reference_no VARCHAR(255) NOT NULL,
                gl_amount DECIMAL(18,2) NOT NULL,
                match_amount DECIMAL(18,2) NOT NULL,
                match_sequence INT NOT NULL,
                PRIMARY KEY (gl_id),
                KEY ix_tmp_acq_gl_match
                    (unique_reference_no, auth_code, rrn, match_amount, match_sequence)
            ) ENGINE = InnoDB;

            CREATE TEMPORARY TABLE tmp_acq_fe_side
            (
                fe_id BIGINT NOT NULL,
                ep_id BIGINT NOT NULL,
                business_date DATE,
                reference_num VARCHAR(50) NOT NULL,
                auth_code VARCHAR(20) NOT NULL,
                utr_no VARCHAR(50) NOT NULL,
                request_amount DECIMAL(18,2) NOT NULL,
                match_sequence INT NOT NULL,
                PRIMARY KEY (fe_id),
                KEY ix_tmp_acq_fe_match
                    (utr_no, auth_code, reference_num, request_amount, match_sequence)
            ) ENGINE = InnoDB;
            """,
            commandTimeout: CommandTimeoutSeconds);

    private static Task ArchiveReversalsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long runId) =>
        connection.ExecuteAsync(
            """
            INSERT INTO acquiring_fe_reversal
            (
                run_id,
                reference_num,
                auth_code,
                original_fe_transaction_id,
                reversal_fe_transaction_id,
                original_request_amount,
                reversal_request_amount
            )
            WITH originals AS
            (
                SELECT id, TRIM(Reference_Num) AS reference_num,
                       TRIM(Auth_Code) AS auth_code, Request_Amount,
                       ROW_NUMBER() OVER (
                           PARTITION BY TRIM(Reference_Num), TRIM(Auth_Code)
                           ORDER BY id) AS pair_number
                FROM acquring_fe_transactions
                WHERE Reversal = 0
                  AND TRIM(IssuerInst) = '9006'
                  AND NULLIF(TRIM(Reference_Num), '') IS NOT NULL
                  AND NULLIF(TRIM(Auth_Code), '') IS NOT NULL
            ),
            reversals AS
            (
                SELECT id, TRIM(Reference_Num) AS reference_num,
                       TRIM(Auth_Code) AS auth_code, Request_Amount,
                       ROW_NUMBER() OVER (
                           PARTITION BY TRIM(Reference_Num), TRIM(Auth_Code)
                           ORDER BY id) AS pair_number
                FROM acquring_fe_transactions
                WHERE Reversal = 1
                  AND TRIM(IssuerInst) = '9006'
                  AND NULLIF(TRIM(Reference_Num), '') IS NOT NULL
                  AND NULLIF(TRIM(Auth_Code), '') IS NOT NULL
            )
            SELECT @RunId, o.reference_num, o.auth_code, o.id, r.id,
                   o.Request_Amount, r.Request_Amount
            FROM originals o
            INNER JOIN reversals r
                ON r.reference_num = o.reference_num
               AND r.auth_code = o.auth_code
               AND r.pair_number = o.pair_number;
            """,
            new { RunId = runId },
            transaction,
            CommandTimeoutSeconds);

    private static Task PopulateEpRrnsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction) =>
        connection.ExecuteAsync(
            """
            INSERT INTO tmp_acq_ep_rrn (rrn, ep_id)
            SELECT TRIM(RRN), MIN(id)
            FROM acquiring_ep
            WHERE NULLIF(TRIM(RRN), '') IS NOT NULL
            GROUP BY TRIM(RRN);
            """,
            transaction: transaction,
            commandTimeout: CommandTimeoutSeconds);

    private static Task PopulateGlSideAsync(
        MySqlConnection connection,
        MySqlTransaction transaction) =>
        connection.ExecuteAsync(
            """
            INSERT INTO tmp_acq_gl_side
            (
                gl_id, ep_id, business_date, rrn, auth_code,
                unique_reference_no, gl_amount, match_amount, match_sequence
            )
            SELECT g.id, e.ep_id, DATE(g.posting_date), TRIM(g.rrn),
                   TRIM(g.auth_code), TRIM(g.unique_reference_no), g.amount,
                   ABS(g.amount),
                   ROW_NUMBER() OVER (
                       PARTITION BY TRIM(g.unique_reference_no),
                                    TRIM(g.auth_code), TRIM(g.rrn), ABS(g.amount)
                       ORDER BY g.id)
            FROM acquiring_gl_transactions g
            INNER JOIN tmp_acq_ep_rrn e ON e.rrn = TRIM(g.rrn)
            WHERE NULLIF(TRIM(g.unique_reference_no), '') IS NOT NULL
              AND NULLIF(TRIM(g.auth_code), '') IS NOT NULL
              AND g.amount IS NOT NULL;
            """,
            transaction: transaction,
            commandTimeout: CommandTimeoutSeconds);

    private static Task PopulateFeSideAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long runId) =>
        connection.ExecuteAsync(
            """
            INSERT INTO tmp_acq_fe_side
            (
                fe_id, ep_id, business_date, reference_num, auth_code,
                utr_no, request_amount, match_sequence
            )
            SELECT f.id, e.ep_id,
                   STR_TO_DATE(CAST(f.Udate AS CHAR), '%Y%m%d'),
                   TRIM(f.Reference_Num), TRIM(f.Auth_Code), TRIM(f.UtrNo),
                   CAST(f.Request_Amount AS DECIMAL(18,2)),
                   ROW_NUMBER() OVER (
                       PARTITION BY TRIM(f.UtrNo), TRIM(f.Auth_Code),
                                    TRIM(f.Reference_Num),
                                    CAST(f.Request_Amount AS DECIMAL(18,2))
                       ORDER BY f.id)
            FROM acquring_fe_transactions f
            INNER JOIN tmp_acq_ep_rrn e ON e.rrn = TRIM(f.Reference_Num)
            LEFT JOIN acquiring_fe_reversal rv
              ON rv.run_id = @RunId
             AND (rv.original_fe_transaction_id = f.id
                  OR rv.reversal_fe_transaction_id = f.id)
            WHERE rv.id IS NULL
              AND TRIM(f.IssuerInst) = '9006'
              AND NULLIF(TRIM(f.Reference_Num), '') IS NOT NULL
              AND NULLIF(TRIM(f.Auth_Code), '') IS NOT NULL
              AND NULLIF(TRIM(f.UtrNo), '') IS NOT NULL
              AND f.Request_Amount IS NOT NULL;
            """,
            new { RunId = runId },
            transaction,
            CommandTimeoutSeconds);

    private static Task InsertMatchedAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long runId) =>
        connection.ExecuteAsync(
            ResultInsertPrefix + """
            SELECT @RunId,
                   SHA2(CONCAT('MATCHED|', g.gl_id, '|', g.ep_id, '|', f.fe_id), 256),
                   'MATCHED', COALESCE(g.business_date, f.business_date),
                   g.gl_id, g.ep_id, f.fe_id, g.rrn, g.auth_code,
                   g.unique_reference_no, g.gl_amount, f.reference_num,
                   f.auth_code, f.utr_no, f.request_amount, NULL
            FROM tmp_acq_gl_side g
            INNER JOIN tmp_acq_fe_side f
              ON f.utr_no = g.unique_reference_no
             AND f.auth_code = g.auth_code
             AND f.reference_num = g.rrn
             AND f.request_amount = g.match_amount
             AND f.match_sequence = g.match_sequence;
            """,
            new { RunId = runId },
            transaction,
            CommandTimeoutSeconds);

    private static Task InsertMissingInCbsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long runId) =>
        connection.ExecuteAsync(
            ResultInsertPrefix + """
            SELECT @RunId,
                   SHA2(CONCAT('MISSING_IN_CBS|0|', f.ep_id, '|', f.fe_id), 256),
                   'MISSING_IN_CBS', f.business_date, NULL, f.ep_id, f.fe_id,
                   f.reference_num, NULL, NULL, NULL, f.reference_num,
                   f.auth_code, f.utr_no, f.request_amount,
                   'No EP-qualified GL/CBS record matched all four fields'
            FROM tmp_acq_fe_side f
            LEFT JOIN tmp_acq_gl_side g
              ON f.utr_no = g.unique_reference_no
             AND f.auth_code = g.auth_code
             AND f.reference_num = g.rrn
             AND f.request_amount = g.match_amount
             AND f.match_sequence = g.match_sequence
            WHERE g.gl_id IS NULL;
            """,
            new { RunId = runId },
            transaction,
            CommandTimeoutSeconds);

    private static Task InsertMissingInBoAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long runId) =>
        connection.ExecuteAsync(
            ResultInsertPrefix + """
            SELECT @RunId,
                   SHA2(CONCAT('MISSING_IN_BO|', g.gl_id, '|', g.ep_id, '|0'), 256),
                   'MISSING_IN_BO', g.business_date, g.gl_id, g.ep_id, NULL,
                   g.rrn, g.auth_code, g.unique_reference_no, g.gl_amount,
                   NULL, NULL, NULL, NULL,
                   'No non-reversal FE/BO record matched all four fields'
            FROM tmp_acq_gl_side g
            LEFT JOIN tmp_acq_fe_side f
              ON f.utr_no = g.unique_reference_no
             AND f.auth_code = g.auth_code
             AND f.reference_num = g.rrn
             AND f.request_amount = g.match_amount
             AND f.match_sequence = g.match_sequence
            WHERE f.fe_id IS NULL;
            """,
            new { RunId = runId },
            transaction,
            CommandTimeoutSeconds);

    private const string ResultInsertPrefix = """
        INSERT INTO acquiring_reconciliation_result
        (
            run_id, result_key, reconciliation_status, business_date,
            gl_transaction_id, ep_transaction_id, fe_transaction_id,
            rrn, gl_auth_code, gl_unique_reference_no, gl_amount,
            fe_reference_num, fe_auth_code, fe_utr_no, fe_request_amount,
            mismatch_reason
        )
        """;

    private static async Task<RunCounts> GetCountsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long runId)
    {
        using var multi = await connection.QueryMultipleAsync(
            """
            SELECT COUNT(*) FROM acquiring_reconciliation_result
             WHERE run_id = @RunId AND reconciliation_status = 'MATCHED';
            SELECT COUNT(*) FROM acquiring_reconciliation_result
             WHERE run_id = @RunId AND reconciliation_status = 'MISSING_IN_CBS';
            SELECT COUNT(*) FROM acquiring_reconciliation_result
             WHERE run_id = @RunId AND reconciliation_status = 'MISSING_IN_BO';
            SELECT COUNT(*) FROM acquiring_fe_reversal WHERE run_id = @RunId;
            """,
            new { RunId = runId },
            transaction,
            CommandTimeoutSeconds);

        return new RunCounts(
            await multi.ReadFirstAsync<int>(),
            await multi.ReadFirstAsync<int>(),
            await multi.ReadFirstAsync<int>(),
            await multi.ReadFirstAsync<int>());
    }

    private static Task MarkRunFailedAsync(
        MySqlConnection connection,
        long runId,
        string error) =>
        connection.ExecuteAsync(
            """
            UPDATE acquiring_reconciliation_run
            SET completed_at = UTC_TIMESTAMP(6), status = 'FAILED',
                error_message = @ErrorMessage
            WHERE id = @RunId;
            """,
            new { RunId = runId, ErrorMessage = error },
            commandTimeout: CommandTimeoutSeconds);

    public async Task<PagedResponse<AcquiringReconciliationResultResponse>> GetResultsAsync(
        AcquiringReconciliationResultsRequest request)
    {
        if (request.RunId <= 0)
            throw new InvalidDataException("RunId must be greater than zero.");

        var status = NormalizeStatus(request.ReconciliationStatus);
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 500);
        var offset = (page - 1) * pageSize;

        const string sql = """
            SELECT id AS Id, run_id AS RunId,
                   reconciliation_status AS ReconciliationStatus,
                   business_date AS BusinessDate,
                   gl_transaction_id AS GlTransactionId,
                   ep_transaction_id AS EpTransactionId,
                   fe_transaction_id AS FeTransactionId,
                   rrn AS Rrn, gl_auth_code AS GlAuthCode,
                   gl_unique_reference_no AS GlUniqueReferenceNo,
                   gl_amount AS GlAmount, fe_reference_num AS FeReferenceNum,
                   fe_auth_code AS FeAuthCode, fe_utr_no AS FeUtrNo,
                   fe_request_amount AS FeRequestAmount,
                   mismatch_reason AS MismatchReason, created_at AS CreatedAt
            FROM acquiring_reconciliation_result
            WHERE run_id = @RunId
              AND (@Status IS NULL OR reconciliation_status = @Status)
            ORDER BY id
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*)
            FROM acquiring_reconciliation_result
            WHERE run_id = @RunId
              AND (@Status IS NULL OR reconciliation_status = @Status);
            """;

        await using var connection = (MySqlConnection)_factory.CreateConnection();
        await connection.OpenAsync();
        using var multi = await connection.QueryMultipleAsync(
            sql,
            new { request.RunId, Status = status, PageSize = pageSize, Offset = offset },
            commandTimeout: CommandTimeoutSeconds);

        var items = (await multi.ReadAsync<AcquiringReconciliationResultResponse>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return Page(items, page, pageSize, total);
    }

    public async Task<PagedResponse<AcquiringReversalResponse>> GetReversalsAsync(
        AcquiringReversalRequest request)
    {
        if (request.RunId <= 0)
            throw new InvalidDataException("RunId must be greater than zero.");

        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 500);
        var offset = (page - 1) * pageSize;

        const string sql = """
            SELECT id AS Id, run_id AS RunId, reference_num AS ReferenceNum,
                   auth_code AS AuthCode,
                   original_fe_transaction_id AS OriginalFeTransactionId,
                   reversal_fe_transaction_id AS ReversalFeTransactionId,
                   original_request_amount AS OriginalRequestAmount,
                   reversal_request_amount AS ReversalRequestAmount,
                   created_at AS CreatedAt
            FROM acquiring_fe_reversal
            WHERE run_id = @RunId
            ORDER BY id
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*) FROM acquiring_fe_reversal WHERE run_id = @RunId;
            """;

        await using var connection = (MySqlConnection)_factory.CreateConnection();
        await connection.OpenAsync();
        using var multi = await connection.QueryMultipleAsync(
            sql,
            new { request.RunId, PageSize = pageSize, Offset = offset },
            commandTimeout: CommandTimeoutSeconds);

        var items = (await multi.ReadAsync<AcquiringReversalResponse>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return Page(items, page, pageSize, total);
    }

    private static string? NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;
        var normalized = status.Trim().ToUpperInvariant();
        return normalized is "MATCHED" or "MISSING_IN_CBS" or "MISSING_IN_BO"
            ? normalized
            : throw new InvalidDataException($"Unsupported reconciliation status '{status}'.");
    }

    private static PagedResponse<T> Page<T>(List<T> items, int page, int pageSize, int total) =>
        new()
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = total == 0 ? 0 : (int)Math.Ceiling((double)total / pageSize)
        };

    private sealed record RunCounts(
        int MatchedCount,
        int MissingInCbsCount,
        int MissingInBoCount,
        int ReversalCount);
}
