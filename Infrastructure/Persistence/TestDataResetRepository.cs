using Dapper;
using MySqlConnector;
using VISA_RECON.API.Application.DTOs.Maintenance;
using VISA_RECON.API.Application.Interfaces;
using VISA_RECON.API.Application.Interfaces.Repository;

namespace VISA_RECON.API.Infrastructure.Persistence;

public sealed class TestDataResetRepository : ITestDataResetRepository
{
    private const int CommandTimeoutSeconds = 720;
    private const string IssuingLockName =
        "visa_recon:issuing_reconciliation";
    private const string AcquiringLockName =
        "visa_recon:acquiring_reconciliation";

    private readonly IDbConnectionFactory _connectionFactory;

    public TestDataResetRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<TestDataResetResponse> DeleteIssuingDataAsync() =>
        DeleteDataAsync(
            "ISSUING",
            IssuingLockName,
            [
                "issuing_reconciliation_run_result",
                "issuing_reversal_transaction",
                "issuing_reconciliation_match",
                "issuing_manual_match_request",
                "issuing_reconciliation_run",
                "issuing_bo_transaction",
                "issuing_cbs_transactions",
                "issuing_upload_batch"
            ]);

    public Task<TestDataResetResponse> DeleteAcquiringDataAsync() =>
        DeleteDataAsync(
            "ACQUIRING",
            AcquiringLockName,
            [
                "acquiring_reconciliation_result",
                "acquiring_fe_reversal",
                "acquiring_reconciliation_run",
                "acquiring_ep",
                "acquring_fe_transactions",
                "acquiring_gl_transactions"
            ]);

    private async Task<TestDataResetResponse> DeleteDataAsync(
        string scope,
        string lockName,
        IReadOnlyList<string> tables)
    {
        await using var connection =
            (MySqlConnection)_connectionFactory.CreateConnection();
        await connection.OpenAsync();

        var lockAcquired = await connection.ExecuteScalarAsync<int>(
            "SELECT GET_LOCK(@LockName, 0);",
            new { LockName = lockName },
            commandTimeout: CommandTimeoutSeconds);

        if (lockAcquired != 1)
        {
            throw new InvalidOperationException(
                $"Cannot clear {scope.ToLowerInvariant()} data while " +
                "reconciliation is running.");
        }

        try
        {
            var deletedRows = new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);

            // Count first so the response can report what TRUNCATE removed.
            // Table names come only from the hard-coded lists above.
            foreach (var table in tables)
            {
                deletedRows[table] = await connection.ExecuteScalarAsync<int>(
                    $"SELECT COUNT(*) FROM `{table}`;",
                    commandTimeout: CommandTimeoutSeconds);
            }

            await connection.ExecuteAsync(
                "SET FOREIGN_KEY_CHECKS = 0;",
                commandTimeout: CommandTimeoutSeconds);

            try
            {
                foreach (var table in tables)
                {
                    await connection.ExecuteAsync(
                        $"TRUNCATE TABLE `{table}`;",
                        commandTimeout: CommandTimeoutSeconds);
                }
            }
            finally
            {
                // FOREIGN_KEY_CHECKS is session-scoped and must always be
                // restored before this pooled connection is returned.
                await connection.ExecuteAsync(
                    "SET FOREIGN_KEY_CHECKS = 1;",
                    commandTimeout: CommandTimeoutSeconds);
            }

            return new TestDataResetResponse
            {
                Scope = scope,
                TotalDeleted = deletedRows.Values.Sum(),
                DeletedRows = deletedRows
            };
        }
        finally
        {
            try
            {
                await connection.ExecuteAsync(
                    "SELECT RELEASE_LOCK(@LockName);",
                    new { LockName = lockName },
                    commandTimeout: CommandTimeoutSeconds);
            }
            catch
            {
                // Closing the connection also releases the named lock.
            }
        }
    }
}
