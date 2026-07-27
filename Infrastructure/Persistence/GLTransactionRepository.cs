using Npgsql;
using NpgsqlTypes;
using VISA_RECON.API.Application.DTOs.GLTransaction;
using VISA_RECON.API.Application.Interfaces;
using VISA_RECON.API.Application.Interfaces.Repositories;

namespace VISA_RECON.API.Infrastructure.Repositories
{
    public class GLTransactionRepository : IGLTransactionRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public GLTransactionRepository(
            IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }


        public async Task<int> InsertBulkAsync(List<UploadRequest> transactions)
        {
            if (transactions == null || transactions.Count == 0)
                return 0;


            await using var connection =
                (NpgsqlConnection)_connectionFactory.CreateConnection();

            await connection.OpenAsync();


            await using var transaction =
                await connection.BeginTransactionAsync();


            try
            {
                await using (var writer =
                    await connection.BeginBinaryImportAsync(@"
                        COPY gl_transaction
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
                        FROM STDIN (FORMAT BINARY)
                    "))
                {
                    foreach (var item in transactions)
                    {
                        await writer.StartRowAsync();

                        await Write(writer, item.AccountNo, NpgsqlDbType.Varchar);

                        await Write(writer, item.PostingDate, NpgsqlDbType.Varchar);

                        await Write(writer, item.ValueDate, NpgsqlDbType.Varchar);

                        await Write(writer, item.BatchId, NpgsqlDbType.Varchar);

                        await Write(writer, item.PostingBranch, NpgsqlDbType.Varchar);

                        await Write(writer, item.UniqueReferenceNo, NpgsqlDbType.Varchar);

                        await Write(writer, item.DebitCredit, NpgsqlDbType.Varchar);

                        await Write(writer, item.Amount, NpgsqlDbType.Varchar);

                        await Write(writer, item.TransactionCode, NpgsqlDbType.Varchar);

                        await Write(writer, item.TransactionName, NpgsqlDbType.Varchar);

                        await Write(writer, item.Currency, NpgsqlDbType.Varchar);

                        await Write(writer, item.TimeStamp, NpgsqlDbType.Varchar);

                        await Write(writer, item.UniqueId, NpgsqlDbType.Varchar);

                        await Write(writer, item.Narrative1, NpgsqlDbType.Varchar);

                        await Write(writer, item.Narrative2, NpgsqlDbType.Varchar);

                        await Write(writer, item.Narrative3, NpgsqlDbType.Varchar);

                        await Write(writer, item.Narrative4, NpgsqlDbType.Varchar);

                        await Write(writer, item.RRN, NpgsqlDbType.Varchar);

                        await Write(writer, item.AuthCode, NpgsqlDbType.Varchar);
                    }

                    // Important: complete COPY before leaving using block
                    await writer.CompleteAsync();
                }


                // COPY is finished and connection is free now
                await transaction.CommitAsync();


                return transactions.Count;
            }
            catch (Exception ex)
            {
                try
                {
                    await transaction.RollbackAsync();
                }
                catch
                {
                    // rollback failure should not hide original exception
                }


                throw new Exception(
                    $"GL Transaction bulk upload failed. No records were saved. Error: {ex.Message}",
                    ex);
            }
        }


        private static async Task Write(
            NpgsqlBinaryImporter writer,
            object? value,
            NpgsqlDbType type)
        {
            if (value == null)
            {
                await writer.WriteNullAsync();
                return;
            }

            await writer.WriteAsync(value, type);
        }
    }
}