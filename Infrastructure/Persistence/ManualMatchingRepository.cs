using System.Data;
using Dapper;
using MySqlConnector;
using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.Reconciliation;
using VISA_RECON.API.Application.Interfaces;
using VISA_RECON.API.Application.Interfaces.Repository;

namespace VISA_RECON.API.Infrastructure.Persistence;

public sealed class ManualMatchingRepository : IManualMatchingRepository
{
    private const int CommandTimeoutSeconds = 300;
    private const string LockName = "visa_recon:issuing_reconciliation";
    private readonly IDbConnectionFactory _connectionFactory;

    public ManualMatchingRepository(IDbConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public async Task<ManualMatchRequestResponse> CreateAsync(
        CreateManualMatchRequest request)
    {
        ValidateCreate(request);
        await using var connection =
            (MySqlConnection)_connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted);

        try
        {
            var pair = await GetPairForUpdateAsync(
                connection, transaction, request.CbsTransactionId,
                request.BoTransactionId);
            ValidatePairIsEligible(pair);

            var conflict = await connection.QueryFirstOrDefaultAsync<long?>(
                """
                SELECT id
                FROM issuing_manual_match_request
                WHERE request_status = 'PENDING'
                  AND (cbs_transaction_id = @CbsId
                       OR bo_transaction_id = @BoId)
                LIMIT 1 FOR UPDATE;
                """,
                new
                {
                    CbsId = request.CbsTransactionId,
                    BoId = request.BoTransactionId
                },
                transaction,
                CommandTimeoutSeconds);

            if (conflict.HasValue)
                throw new InvalidOperationException(
                    $"A pending manual match request ({conflict.Value}) already uses one of these transactions.");

            await connection.ExecuteAsync(
                """
                INSERT INTO issuing_manual_match_request
                (
                    cbs_transaction_id, bo_transaction_id, request_status,
                    requested_by, requested_at, reason, evidence_reference
                )
                VALUES
                (
                    @CbsId, @BoId, 'PENDING', @RequestedBy, @RequestedAt,
                    @Reason, @EvidenceReference
                );
                """,
                new
                {
                    CbsId = request.CbsTransactionId,
                    BoId = request.BoTransactionId,
                    RequestedBy = request.RequestedBy.Trim(),
                    RequestedAt = DateTime.UtcNow,
                    Reason = request.Reason.Trim(),
                    EvidenceReference = NullIfEmpty(request.EvidenceReference)
                },
                transaction,
                CommandTimeoutSeconds);

            var id = await connection.ExecuteScalarAsync<long>(
                "SELECT LAST_INSERT_ID();", transaction: transaction,
                commandTimeout: CommandTimeoutSeconds);
            await transaction.CommitAsync();
            return await GetByIdAsync(connection, id);
        }
        catch
        {
            try { await transaction.RollbackAsync(); } catch { }
            throw;
        }
    }

    public async Task<ManualMatchConfirmationResponse> ApproveAsync(
        long requestId, ReviewManualMatchRequest request)
    {
        ValidateReview(requestId, request);
        await using var connection =
            (MySqlConnection)_connectionFactory.CreateConnection();
        await connection.OpenAsync();

        var lockAcquired = await connection.ExecuteScalarAsync<int>(
            "SELECT GET_LOCK(@LockName, 0);", new { LockName },
            commandTimeout: CommandTimeoutSeconds);
        if (lockAcquired != 1)
            throw new InvalidOperationException(
                "Automatic or manual issuing reconciliation is currently running.");

        try
        {
            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted);
            try
            {
                var manualRequest = await connection.QuerySingleOrDefaultAsync<ManualRequestRow>(
                    """
                    SELECT id AS Id, cbs_transaction_id AS CbsTransactionId,
                           bo_transaction_id AS BoTransactionId,
                           request_status AS RequestStatus
                    FROM issuing_manual_match_request
                    WHERE id = @RequestId
                    FOR UPDATE;
                    """,
                    new { RequestId = requestId }, transaction,
                    CommandTimeoutSeconds)
                    ?? throw new InvalidDataException(
                        $"Manual match request {requestId} was not found.");

                if (manualRequest.RequestStatus != "PENDING")
                    throw new InvalidOperationException(
                        $"Manual match request {requestId} is already {manualRequest.RequestStatus}.");

                var pair = await GetPairForUpdateAsync(
                    connection, transaction, manualRequest.CbsTransactionId,
                    manualRequest.BoTransactionId);
                ValidatePairIsEligible(pair);

                var now = DateTime.UtcNow;
                await connection.ExecuteAsync(
                    """
                    INSERT INTO issuing_reconciliation_run
                    (
                        reconciliation_date, started_at, status, run_type,
                        rule_version, cbs_cutoff_id, bo_cutoff_id
                    )
                    VALUES
                    (
                        @ReconciliationDate, @Now, 'RUNNING', 'MANUAL',
                        'MANUAL_V1', @CbsId, @BoId
                    );
                    """,
                    new
                    {
                        ReconciliationDate = DateTime.Today,
                        Now = now,
                        CbsId = pair.CbsId,
                        BoId = pair.BoId
                    },
                    transaction,
                    CommandTimeoutSeconds);
                var runId = await connection.ExecuteScalarAsync<long>(
                    "SELECT LAST_INSERT_ID();", transaction: transaction,
                    commandTimeout: CommandTimeoutSeconds);

                await connection.ExecuteAsync(
                    """
                    INSERT INTO issuing_reconciliation_match
                    (
                        run_id, cbs_transaction_id, bo_transaction_id,
                        reconciliation_currency, transaction_category,
                        match_rule, rule_version, matched_at,
                        manual_match_request_id, match_status
                    )
                    VALUES
                    (
                        @RunId, @CbsId, @BoId, @Currency, @Category,
                        'MANUAL', 'MANUAL_V1', @Now, @RequestId, 'ACTIVE'
                    );
                    """,
                    new
                    {
                        RunId = runId,
                        pair.CbsId,
                        pair.BoId,
                        pair.Currency,
                        pair.Category,
                        Now = now,
                        RequestId = requestId
                    },
                    transaction,
                    CommandTimeoutSeconds);
                var matchId = await connection.ExecuteScalarAsync<long>(
                    "SELECT LAST_INSERT_ID();", transaction: transaction,
                    commandTimeout: CommandTimeoutSeconds);

                await connection.ExecuteAsync(
                    """
                    UPDATE issuing_cbs_transactions
                    SET reconciliation_status = 'MATCHED',
                        last_attempted_at = @Now,
                        last_reconciliation_run_id = @RunId,
                        matched_at = @Now, match_rule = 'MANUAL'
                    WHERE id = @CbsId;

                    UPDATE issuing_bo_transaction
                    SET reconciliation_status = 'MATCHED',
                        last_attempted_at = @Now,
                        last_reconciliation_run_id = @RunId,
                        matched_at = @Now, match_rule = 'MANUAL'
                    WHERE id = @BoId;

                    INSERT INTO issuing_reconciliation_run_result
                    (
                        run_id, result_status, cbs_transaction_id,
                        bo_transaction_id, match_id,
                        reconciliation_currency, transaction_category,
                        business_date, created_at
                    )
                    VALUES
                    (
                        @RunId, 'MATCHED', @CbsId, @BoId, @MatchId,
                        @Currency, @Category, @BusinessDate, @Now
                    );

                    UPDATE issuing_manual_match_request
                    SET request_status = 'APPROVED', reviewed_by = @ReviewedBy,
                        reviewed_at = @Now, review_note = @ReviewNote,
                        approved_run_id = @RunId
                    WHERE id = @RequestId;

                    UPDATE issuing_reconciliation_run
                    SET completed_at = @Now, status = 'COMPLETED',
                        manual_match_count = 1
                    WHERE id = @RunId;
                    """,
                    new
                    {
                        RunId = runId,
                        MatchId = matchId,
                        pair.CbsId,
                        pair.BoId,
                        pair.Currency,
                        pair.Category,
                        pair.BusinessDate,
                        Now = now,
                        RequestId = requestId,
                        ReviewedBy = request.ReviewedBy.Trim(),
                        ReviewNote = NullIfEmpty(request.ReviewNote)
                    },
                    transaction,
                    CommandTimeoutSeconds);

                await transaction.CommitAsync();
                return new ManualMatchConfirmationResponse
                {
                    RequestId = requestId,
                    RunId = runId,
                    MatchId = matchId,
                    CbsTransactionId = pair.CbsId,
                    BoTransactionId = pair.BoId,
                    MatchedAt = now
                };
            }
            catch
            {
                try { await transaction.RollbackAsync(); } catch { }
                throw;
            }
        }
        finally
        {
            try
            {
                await connection.ExecuteAsync(
                    "SELECT RELEASE_LOCK(@LockName);", new { LockName },
                    commandTimeout: CommandTimeoutSeconds);
            }
            catch { }
        }
    }

    public async Task<ManualMatchRequestResponse> RejectAsync(
        long requestId, ReviewManualMatchRequest request)
    {
        ValidateReview(requestId, request);
        await using var connection =
            (MySqlConnection)_connectionFactory.CreateConnection();
        await connection.OpenAsync();
        var changed = await connection.ExecuteAsync(
            """
            UPDATE issuing_manual_match_request
            SET request_status = 'REJECTED', reviewed_by = @ReviewedBy,
                reviewed_at = @ReviewedAt, review_note = @ReviewNote
            WHERE id = @RequestId AND request_status = 'PENDING';
            """,
            new
            {
                RequestId = requestId,
                ReviewedBy = request.ReviewedBy.Trim(),
                ReviewedAt = DateTime.UtcNow,
                ReviewNote = NullIfEmpty(request.ReviewNote)
            },
            commandTimeout: CommandTimeoutSeconds);
        if (changed != 1)
            throw new InvalidOperationException(
                $"Manual match request {requestId} was not found or is no longer pending.");
        return await GetByIdAsync(connection, requestId);
    }

    public async Task<PagedResponse<ManualMatchRequestResponse>> GetAsync(
        ManualMatchListRequest request)
    {
        var status = NormalizeRequestStatus(request.RequestStatus);
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 500);
        var offset = (page - 1) * pageSize;
        await using var connection =
            (MySqlConnection)_connectionFactory.CreateConnection();
        await connection.OpenAsync();
        using var multi = await connection.QueryMultipleAsync(
            $"""
            {SelectRequestSql}
            WHERE (@Status IS NULL OR request_status = @Status)
            ORDER BY id DESC LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*) FROM issuing_manual_match_request
            WHERE (@Status IS NULL OR request_status = @Status);
            """,
            new { Status = status, PageSize = pageSize, Offset = offset },
            commandTimeout: CommandTimeoutSeconds);
        var items = (await multi.ReadAsync<ManualMatchRequestResponse>()).ToList();
        var total = await multi.ReadSingleAsync<int>();
        return new PagedResponse<ManualMatchRequestResponse>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = total == 0 ? 0 : (int)Math.Ceiling((double)total / pageSize)
        };
    }

    private static async Task<PairRow> GetPairForUpdateAsync(
        MySqlConnection connection, MySqlTransaction transaction,
        long cbsId, long boId)
    {
        var row = await connection.QuerySingleOrDefaultAsync<PairRow>(
            """
            SELECT c.id AS CbsId, b.id AS BoId,
                   c.reconciliation_status AS CbsStatus,
                   b.reconciliation_status AS BoStatus,
                   c.reconciliation_currency AS CbsCurrency,
                   b.reconciliation_currency AS BoCurrency,
                   c.transaction_category AS CbsCategory,
                   b.transaction_category AS BoCategory,
                   c.reconciliation_currency AS Currency,
                   c.transaction_category AS Category,
                   DATE(c.posting_date) AS BusinessDate
            FROM issuing_cbs_transactions AS c
            INNER JOIN issuing_bo_transaction AS b ON b.id = @BoId
            WHERE c.id = @CbsId
            FOR UPDATE;
            """,
            new { CbsId = cbsId, BoId = boId }, transaction,
            CommandTimeoutSeconds);
        return row ?? throw new InvalidDataException(
            "The selected CBS or BO transaction was not found.");
    }

    private static void ValidatePairIsEligible(PairRow pair)
    {
        if (pair.CbsStatus is not ("PENDING" or "UNMATCHED"))
            throw new InvalidOperationException(
                $"CBS transaction {pair.CbsId} has status {pair.CbsStatus} and cannot be manually matched.");
        if (pair.BoStatus is not ("PENDING" or "UNMATCHED"))
            throw new InvalidOperationException(
                $"BO transaction {pair.BoId} has status {pair.BoStatus} and cannot be manually matched.");
        if (!string.Equals(pair.CbsCurrency, pair.BoCurrency,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(pair.CbsCategory, pair.BoCategory,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Manual matching requires the CBS and BO reconciliation currency and category to be the same.");
    }

    private static async Task<ManualMatchRequestResponse> GetByIdAsync(
        MySqlConnection connection, long id) =>
        await connection.QuerySingleAsync<ManualMatchRequestResponse>(
            $"""{SelectRequestSql} WHERE id = @Id;""",
            new { Id = id }, commandTimeout: CommandTimeoutSeconds);

    private static void ValidateCreate(CreateManualMatchRequest request)
    {
        if (request.CbsTransactionId <= 0 || request.BoTransactionId <= 0)
            throw new InvalidDataException(
                "CbsTransactionId and BoTransactionId must be greater than zero.");
        if (string.IsNullOrWhiteSpace(request.RequestedBy))
            throw new InvalidDataException("RequestedBy is required.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new InvalidDataException("Reason is required.");
        if (request.RequestedBy.Trim().Length > 100
            || request.Reason.Trim().Length > 1000
            || (request.EvidenceReference?.Trim().Length ?? 0) > 500)
            throw new InvalidDataException("One or more manual request fields exceed the database limit.");
    }

    private static void ValidateReview(long requestId, ReviewManualMatchRequest request)
    {
        if (requestId <= 0)
            throw new InvalidDataException("RequestId must be greater than zero.");
        if (string.IsNullOrWhiteSpace(request.ReviewedBy))
            throw new InvalidDataException("ReviewedBy is required.");
        if (request.ReviewedBy.Trim().Length > 100
            || (request.ReviewNote?.Trim().Length ?? 0) > 1000)
            throw new InvalidDataException("One or more review fields exceed the database limit.");
    }

    private static string? NormalizeRequestStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var status = value.Trim().ToUpperInvariant();
        return status is "PENDING" or "APPROVED" or "REJECTED" or "CANCELLED"
            ? status
            : throw new InvalidDataException($"Unsupported manual request status '{value}'.");
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private const string SelectRequestSql = """
        SELECT id AS Id, cbs_transaction_id AS CbsTransactionId,
               bo_transaction_id AS BoTransactionId,
               request_status AS RequestStatus, requested_by AS RequestedBy,
               requested_at AS RequestedAt, reason AS Reason,
               evidence_reference AS EvidenceReference,
               reviewed_by AS ReviewedBy, reviewed_at AS ReviewedAt,
               review_note AS ReviewNote, approved_run_id AS ApprovedRunId
        FROM issuing_manual_match_request
        """;

    private sealed class ManualRequestRow
    {
        public long CbsTransactionId { get; init; }
        public long BoTransactionId { get; init; }
        public string RequestStatus { get; init; } = string.Empty;
    }

    private sealed class PairRow
    {
        public long CbsId { get; init; }
        public long BoId { get; init; }
        public string CbsStatus { get; init; } = string.Empty;
        public string BoStatus { get; init; } = string.Empty;
        public string CbsCurrency { get; init; } = string.Empty;
        public string BoCurrency { get; init; } = string.Empty;
        public string CbsCategory { get; init; } = string.Empty;
        public string BoCategory { get; init; } = string.Empty;
        public string Currency { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public DateTime? BusinessDate { get; init; }
    }
}
