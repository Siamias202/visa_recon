using Dapper;
using MySqlConnector;
using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.AcquiringTransaction;
using VISA_RECON.API.Application.DTOs.GLTransaction;
using VISA_RECON.API.Application.Interfaces;
using VISA_RECON.API.Application.Interfaces.Repository;

namespace VISA_RECON.API.Infrastructure.Persistence;

public sealed class AcquiringTransactionRepository : IAcquiringTransactionRepository
{
    private const int Timeout = 720;
    private readonly IDbConnectionFactory _factory;
    public AcquiringTransactionRepository(IDbConnectionFactory factory) => _factory = factory;

    public Task<int> InsertGlAsync(IEnumerable<UploadGLRequest> rows) => InsertAsync("""
        INSERT INTO acquiring_gl_transactions
        (account_no, posting_date, value_date, batch_id, posting_branch, unique_reference_no,
         debit_credit, amount, transaction_code, transaction_name, currency, time_stamp, unique_id,
         narrative_1, narrative_2, narrative_3, narrative_4, rrn, auth_code)
        VALUES (@AccountNo, @PostingDate, @ValueDate, @BatchId, @PostingBranch, @UniqueReferenceNo,
         @DebitCredit, @Amount, @TransactionCode, @TransactionName, @Currency, @TimeStamp, @UniqueId,
         @Narrative1, @Narrative2, @Narrative3, @Narrative4, @RRN, @AuthCode);
        """, rows);

    public Task<int> InsertFeAsync(IEnumerable<AcquiringFeTransaction> rows) => InsertAsync("""
        INSERT INTO acquring_fe_transactions
        (Atm_Id, Reversal, Request_Amount, BILLS1, BILLS2, BILLS3, BILLS4, Udate, `Time`,
         UtrNo, IssuerInst, Reference_Num, Auth_Code, acct1, Hpan_Card)
        SELECT @AtmId, @Reversal, @RequestAmount, @Bills1, @Bills2, @Bills3, @Bills4, @Udate, @Time,
         @UtrNo, @IssuerInst, @ReferenceNum, @AuthCode, @Acct1, @HpanCard
        WHERE TRIM(@IssuerInst) = '9006';
        """, rows);

    public Task<int> InsertEpAsync(IEnumerable<AcquiringEpTransaction> rows) => InsertAsync("""
        INSERT INTO acquiring_ep
        (PAN, RRN, ACQ, INTEGRATEDP, AYMEN, TSYSTE, M, AMOUNTBDT, CURRENCY, AMOUNTUSD)
        VALUES (@Pan, @Rrn, @Acq, @Integratedp, @Aymen, @Tsyste, @M, @AmountBdt, @Currency, @AmountUsd);
        """, rows);

    private async Task<int> InsertAsync<T>(string sql, IEnumerable<T> source)
    {
        var rows = source.ToList();
        if (rows.Count == 0) return 0;
        await using var connection = (MySqlConnection)_factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var inserted = 0;
            foreach (var batch in rows.Chunk(1000))
                inserted += await connection.ExecuteAsync(sql, batch, transaction, Timeout);
            await transaction.CommitAsync();
            return inserted;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public Task<PagedResponse<GLTransactionDetailsResponse>> GetGlAsync(AcquiringDetailsRequest request) =>
        QueryAsync<GLTransactionDetailsResponse>("""
            SELECT account_no AS AccountNo, DATE_FORMAT(posting_date,'%Y-%m-%d') AS PostingDate,
              DATE_FORMAT(value_date,'%Y-%m-%d') AS ValueDate, batch_id AS BatchId,
              posting_branch AS PostingBranch, unique_reference_no AS UniqueReferenceNo,
              debit_credit AS DebitCredit, amount AS Amount, transaction_code AS TransactionCode,
              transaction_name AS TransactionName, currency AS Currency,
              CAST(time_stamp AS CHAR) AS TimeStamp, unique_id AS UniqueId,
              narrative_1 AS Narrative1, narrative_2 AS Narrative2, narrative_3 AS Narrative3,
              narrative_4 AS Narrative4, rrn AS RRN, auth_code AS AuthCode
            FROM acquiring_gl_transactions
            """, """
            CONCAT_WS(' ', account_no, unique_reference_no, auth_code, rrn, transaction_name)
            """, request);

    public Task<PagedResponse<AcquiringFeTransaction>> GetFeAsync(AcquiringDetailsRequest request) =>
        QueryAsync<AcquiringFeTransaction>("""
            SELECT id AS Id, Atm_Id AS AtmId, Reversal AS Reversal, Request_Amount AS RequestAmount,
              BILLS1 AS Bills1, BILLS2 AS Bills2, BILLS3 AS Bills3, BILLS4 AS Bills4,
              Udate AS Udate, `Time` AS Time, UtrNo AS UtrNo, IssuerInst AS IssuerInst,
              Reference_Num AS ReferenceNum, Auth_Code AS AuthCode, acct1 AS Acct1, Hpan_Card AS HpanCard
            FROM acquring_fe_transactions
            """, "CONCAT_WS(' ', Atm_Id, UtrNo, Reference_Num, Auth_Code, acct1, Hpan_Card)", request);

    public Task<PagedResponse<AcquiringEpTransaction>> GetEpAsync(AcquiringDetailsRequest request) =>
        QueryAsync<AcquiringEpTransaction>("""
            SELECT id AS Id, PAN AS Pan, RRN AS Rrn, ACQ AS Acq, INTEGRATEDP AS Integratedp,
              AYMEN AS Aymen, TSYSTE AS Tsyste, M AS M, AMOUNTBDT AS AmountBdt,
              CURRENCY AS Currency, AMOUNTUSD AS AmountUsd
            FROM acquiring_ep
            """, "CONCAT_WS(' ', PAN, RRN, ACQ, INTEGRATEDP, CURRENCY)", request);

    private async Task<PagedResponse<T>> QueryAsync<T>(string selectSql, string searchExpression,
        AcquiringDetailsRequest request)
    {
        var page = Math.Max(request?.Page ?? 1, 1);
        var pageSize = Math.Clamp(request?.PageSize ?? 20, 1, 500);
        var offset = (page - 1) * pageSize;
        var search = string.IsNullOrWhiteSpace(request?.SearchQuery) ? null : $"%{request.SearchQuery.Trim()}%";
        var fromIndex = selectSql.IndexOf("FROM ", StringComparison.OrdinalIgnoreCase);
        var fromSql = selectSql[fromIndex..];
        var where = $" WHERE (@Search IS NULL OR {searchExpression} LIKE @Search) ";
        var sql = $"{selectSql} {where} ORDER BY 1 DESC LIMIT @PageSize OFFSET @Offset; " +
                  $"SELECT COUNT(*) {fromSql} {where};";
        await using var connection = (MySqlConnection)_factory.CreateConnection();
        await connection.OpenAsync();
        using var multi = await connection.QueryMultipleAsync(sql,
            new { Search = search, PageSize = pageSize, Offset = offset }, commandTimeout: Timeout);
        var items = (await multi.ReadAsync<T>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new PagedResponse<T>
        {
            Items = items, Page = page, PageSize = pageSize, TotalItems = total,
            TotalPages = total == 0 ? 0 : (int)Math.Ceiling((double)total / pageSize)
        };
    }
}
