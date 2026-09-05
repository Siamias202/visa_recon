namespace VISA_RECON.API.Application.Constants
{
    public class ColumnSelection
    {

        public static class ColumnSelectionQuery
        {
            public const string CbsSelectColumns = """
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

            public const string BoSelectColumns = """
                                                    b.session_id AS SESSION_ID,
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

            public const string AgeBucketExpression = """
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
        }
    }
}
