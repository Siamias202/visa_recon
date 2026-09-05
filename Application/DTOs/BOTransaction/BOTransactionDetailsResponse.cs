namespace VISA_RECON.API.Application.DTOs.BOTransaction;

public class BOTransactionDetailsResponse
{
    public long Id { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public DateTime? ReconciliationBusinessDate { get; set; }

    public string SESSION_ID { get; set; } = string.Empty;
    public string BO_OPER_ID { get; set; } = string.Empty;

    public string EP_STTL_DATE { get; set; } = string.Empty;
    public string RUN_DATE { get; set; } = string.Empty;

    public string TRX_TYPE { get; set; } = string.Empty;
    public string MESSAGE_TYPE { get; set; } = string.Empty;
    public string CLR_STATUS { get; set; } = string.Empty;
    public string CONTRACT_TYPE { get; set; } = string.Empty;

    public string CARD_NUMBER { get; set; } = string.Empty;
    public string ACCOUNT_NUMBER { get; set; } = string.Empty;
    public string SENDER_ACCOUNT_NUMBER { get; set; } = string.Empty;

    public string AUTH_CODE { get; set; } = string.Empty;
    public string ARN { get; set; } = string.Empty;

    public string TRANS_DATE { get; set; } = string.Empty;

    public decimal? CLR_TXN_AMOUNT { get; set; }
    public string TXN_CURRENCY { get; set; } = string.Empty;

    public string BILL_AMT { get; set; } = string.Empty;
    public string ACCT_CURR { get; set; } = string.Empty;

    public decimal? STTL_AMOUNT { get; set; }
    public short? ST_REV { get; set; }

    public string MATCH_STATUS { get; set; } = string.Empty;

    public string AUTH_ID { get; set; } = string.Empty;

    public string MCC { get; set; } = string.Empty;
    public string MERCHANT_NUMBER { get; set; } = string.Empty;
    public string? TERMINAL_NUMBER { get; set; }
    public string? MERCHANT_NAME { get; set; }
    public string? MERCHANT_CITY { get; set; }
    public string? MERCHANT_COUNTRY { get; set; }

    public string? AUTH_OPR_ID { get; set; }
    public string? BASE_II_ID { get; set; }

    public string TRANSACTION_DATE { get; set; } = string.Empty;

    public string? AUTH_CARD_NUMBER { get; set; }

    public short? REVERSAL_FLAG { get; set; }

    public string TXN_AMOUNT { get; set; } = string.Empty;

    public string AUTH_CURRENCY { get; set; } = string.Empty;

    public decimal? BILLING_AMOUNT { get; set; }
    public decimal? FEES { get; set; }

    public string? BILLING_CURRENCY { get; set; }

    public string? STATUS { get; set; }

    // IMPORTANT:
    // This is TXN_TYPE, not TRX_TYPE.
    public string? TXN_TYPE { get; set; }

    public string? AUTH_MESSAGE_TYPE { get; set; }
    public string? AUTH_MCC { get; set; }
    public string? AUTH_MID { get; set; }
    public string? AUTH_MERCHANT_NAME { get; set; }
    public string? AUTH_CITY { get; set; }
    public string? AUTH_COUNTRY { get; set; }
    public string? AUTH_TID { get; set; }
    public string? AUTH_ACCT_UMBER { get; set; }
    public string? POS_COND_CODE { get; set; }
    public string UTRNNO { get; set; } = string.Empty;
    public string? TRACE_NUMBER { get; set; }
    public string? TRACE_TO_CBS { get; set; }
    public string? RRN { get; set; }
    public string? AUTH { get; set; }

    public long? UploadBatchId { get; set; }

    public DateTime? UploadedAt { get; set; }

    public string ReconciliationCurrency { get; set; } = string.Empty;

    public string TransactionCategory { get; set; } = string.Empty;

    public string ReconciliationStatus { get; set; } = string.Empty;

    public DateTime? MatchedAt { get; set; }

    public string? MatchRule { get; set; }
}
