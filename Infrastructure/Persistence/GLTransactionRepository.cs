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


        public async Task<int> InsertBulkAsync(
            IEnumerable<UploadGLRequest> transactions)
        {
            if (transactions == null)
                return 0;


            await using var connection =
                (NpgsqlConnection)_connectionFactory.CreateConnection();

            await connection.OpenAsync();


            await using var transaction =
                await connection.BeginTransactionAsync();


            try
            {
                int insertedCount = 0;


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


                        await Write(writer, item.AccountNo);
                        await Write(writer, item.PostingDate);
                        await Write(writer, item.ValueDate);
                        await Write(writer, item.BatchId);
                        await Write(writer, item.PostingBranch);
                        await Write(writer, item.UniqueReferenceNo);
                        await Write(writer, item.DebitCredit);
                        await Write(writer, item.Amount);
                        await Write(writer, item.TransactionCode);
                        await Write(writer, item.TransactionName);
                        await Write(writer, item.Currency);
                        await Write(writer, item.TimeStamp);
                        await Write(writer, item.UniqueId);
                        await Write(writer, item.Narrative1);
                        await Write(writer, item.Narrative2);
                        await Write(writer, item.Narrative3);
                        await Write(writer, item.Narrative4);
                        await Write(writer, item.RRN);
                        await Write(writer, item.AuthCode);


                        insertedCount++;
                    }


                    await writer.CompleteAsync();
                }


                await transaction.CommitAsync();


                return insertedCount;
            }
            catch (Exception ex)
            {
                try
                {
                    await transaction.RollbackAsync();
                }
                catch
                {
                }


                throw new Exception(
                    $"GL Transaction bulk upload failed. No records were saved. Error: {ex.Message}",
                    ex);
            }
        }


        private static async Task Write(
            NpgsqlBinaryImporter writer,
            string? value)
        {
            if (value == null)
            {
                await writer.WriteNullAsync();
                return;
            }


            await writer.WriteAsync(
                value,
                NpgsqlDbType.Varchar);
        }
    }
}