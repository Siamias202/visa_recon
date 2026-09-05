using System.Text.Json;
using Dapper;
using MySqlConnector;
using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.Constants;
using VISA_RECON.API.Application.DTOs.Reconciliation;
using VISA_RECON.API.Application.Helper;
using VISA_RECON.API.Application.Interfaces;
using VISA_RECON.API.Application.Interfaces.Repository;

namespace VISA_RECON.API.Infrastructure.Persistence;

public sealed class ReportingRepository : IReportingRepository
{
    private const int CommandTimeoutSeconds = 720;

    private readonly IDbConnectionFactory _connectionFactory;

    public ReportingRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PagedResponse<ReconciliationStoredResultResponse>>
        GetResultsAsync(ReconciliationResultsRequest request)
    {
        if (request.RunId < 0) throw new InvalidDataException("RunId cannot be negative.");
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
        if (dateFrom > dateTo) throw new InvalidDataException("DateFrom cannot be later than DateTo.");
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 500);
        var offset = (page - 1) * pageSize;

        await using var connection =
            (MySqlConnection)_connectionFactory.CreateConnection();

        await connection.OpenAsync();

        const string findRunSql = """
            SELECT id FROM issuing_reconciliation_run
            WHERE status = 'COMPLETED' AND (@RunId = 0 OR id = @RunId)
            ORDER BY id DESC LIMIT 1;
            """;
        var runId = await connection.QueryFirstOrDefaultAsync<long?>(
            findRunSql, new { request.RunId }, commandTimeout: CommandTimeoutSeconds);
        if (!runId.HasValue)
            throw new InvalidDataException(request.RunId == 0
                ? "No completed reconciliation run was found."
                : $"Completed reconciliation run {request.RunId} was not found.");

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
                RunId = runId.Value,
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
                AccountNumbers = accountNumbers.Length == 0 ? [""] : accountNumbers,
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
                    {ColumnSelection.ColumnSelectionQuery.AgeBucketExpression} AS AgeBucket,
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
                    {ColumnSelection.ColumnSelectionQuery.AgeBucketExpression} AS AgeBucket
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
                {ColumnSelection.ColumnSelectionQuery.AgeBucketExpression} AS AgeBucket,
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
