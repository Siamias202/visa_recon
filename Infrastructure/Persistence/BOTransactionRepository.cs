using Npgsql;
using NpgsqlTypes;
using VISA_RECON.API.Application.DTOs.BOTransaction;
using VISA_RECON.API.Application.Interfaces;
using VISA_RECON.API.Application.Interfaces.Repository;

namespace VISA_RECON.API.Infrastructure.Persistence
{
    public class BOTransactionRepository : IBOTransactionRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public BOTransactionRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<int> InsertBulkAsync(IEnumerable<UploadBORequest> transactions)
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

                await using (var writer = await connection.BeginBinaryImportAsync(@"
                    COPY bo_transaction
                    (
                        session_id,
                        bo_oper_id,
                        ep_sttl_date,
                        run_date,
                        trx_type,
                        message_type,
                        clr_status,
                        contract_type,
                        card_number,
                        account_number,
                        sender_account_number,
                        auth_code,
                        arn,
                        trans_date,
                        clr_txn_amount,
                        txn_currency,
                        bill_amt,
                        acct_curr,
                        sttl_amount,
                        st_rev,
                        match_status,
                        auth_id,
                        mcc,
                        merchant_number,
                        terminal_number,
                        merchant_name,
                        merchant_city,
                        merchant_country,
                        auth_opr_id,
                        base_ii_id,
                        transaction_date,
                        auth_card_number,
                        reversal_flag,
                        txn_amount,
                        auth_currency,
                        billing_amount,
                        fees,
                        billing_currency,
                        status,
                        txn_type,
                        auth_message_type,
                        auth_mcc,
                        auth_mid,
                        auth_merchant_name,
                        auth_city,
                        auth_country,
                        auth_tid,
                        auth_acct_umber,
                        pos_cond_code,
                        utrnno,
                        trace_number,
                        trace_to_cbs,
                        rrn,
                        auth,
                        fe,
                        vrol,
                        status_text,
                        update_flag
                    )
                    FROM STDIN (FORMAT BINARY)
                "))
                {
                    foreach (var item in transactions)
                    {
                        await writer.StartRowAsync();

                        await Write(writer, item.SESSION_ID);
                        await Write(writer, item.BO_OPER_ID);
                        await Write(writer, item.EP_STTL_DATE);
                        await Write(writer, item.RUN_DATE);
                        await Write(writer, item.TRX_TYPE);
                        await Write(writer, item.MESSAGE_TYPE);
                        await Write(writer, item.CLR_STATUS);
                        await Write(writer, item.CONTRACT_TYPE);
                        await Write(writer, item.CARD_NUMBER);
                        await Write(writer, item.ACCOUNT_NUMBER);
                        await Write(writer, item.SENDER_ACCOUNT_NUMBER);
                        await Write(writer, item.AUTH_CODE);
                        await Write(writer, item.ARN);
                        await Write(writer, item.TRANS_DATE);
                        await Write(writer, item.CLR_TXN_AMOUNT);
                        await Write(writer, item.TXN_CURRENCY);
                        await Write(writer, item.BILL_AMT);
                        await Write(writer, item.ACCT_CURR);
                        await Write(writer, item.STTL_AMOUNT);
                        await Write(writer, item.ST_REV);
                        await Write(writer, item.MATCH_STATUS);
                        await Write(writer, item.AUTH_ID);
                        await Write(writer, item.MCC);
                        await Write(writer, item.MERCHANT_NUMBER);
                        await Write(writer, item.TERMINAL_NUMBER);
                        await Write(writer, item.MERCHANT_NAME);
                        await Write(writer, item.MERCHANT_CITY);
                        await Write(writer, item.MERCHANT_COUNTRY);
                        await Write(writer, item.AUTH_OPR_ID);
                        await Write(writer, item.BASE_II_ID);
                        await Write(writer, item.TRANSACTION_DATE);
                        await Write(writer, item.AUTH_CARD_NUMBER);
                        await Write(writer, item.REVERSAL_FLAG);
                        await Write(writer, item.TXN_AMOUNT);
                        await Write(writer, item.AUTH_CURRENCY);
                        await Write(writer, item.BILLING_AMOUNT);
                        await Write(writer, item.FEES);
                        await Write(writer, item.BILLING_CURRENCY);
                        await Write(writer, item.STATUS);
                        await Write(writer, item.TXN_TYPE);
                        await Write(writer, item.AUTH_MESSAGE_TYPE);
                        await Write(writer, item.AUTH_MCC);
                        await Write(writer, item.AUTH_MID);
                        await Write(writer, item.AUTH_MERCHANT_NAME);
                        await Write(writer, item.AUTH_CITY);
                        await Write(writer, item.AUTH_COUNTRY);
                        await Write(writer, item.AUTH_TID);
                        await Write(writer, item.AUTH_ACCT_UMBER);
                        await Write(writer, item.POS_COND_CODE);
                        await Write(writer, item.UTRNNO);
                        await Write(writer, item.TRACE_NUMBER);
                        await Write(writer, item.TRACE_TO_CBS);
                        await Write(writer, item.RRN);
                        await Write(writer, item.AUTH);
                        await Write(writer, item.FE);
                        await Write(writer, item.VROL);
                        await Write(writer, item.Status);
                        await Write(writer, item.update);

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
                    $"BO Transaction bulk upload failed. No records were saved. Error: {ex.Message}",
                    ex);
            }
        }

        private static async Task Write(
            NpgsqlBinaryImporter writer,
            string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                await writer.WriteNullAsync();
                return;
            }

            await writer.WriteAsync(value, NpgsqlDbType.Varchar);
        }
    }
}