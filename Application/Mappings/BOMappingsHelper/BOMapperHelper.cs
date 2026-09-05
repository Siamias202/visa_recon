using CsvHelper.Configuration;
using VISA_RECON.API.Application.DTOs.BOTransaction;

namespace VISA_RECON.API.Application.Mappings.BOMappingsHelper
{
    public class UploadBORequestHelper : ClassMap<UploadBORequest>
    {
        public UploadBORequestHelper()
        {
            Map(m => m.SESSION_ID).Name("SESSION_ID");
            Map(m => m.BO_OPER_ID).Name("BO_OPER_ID");
            Map(m => m.EP_STTL_DATE).Name("EP_STTL_DATE");
            Map(m => m.RUN_DATE).Name("RUN_DATE");
            Map(m => m.TRX_TYPE).Name("TRX_TYPE");
            Map(m => m.MESSAGE_TYPE).Name("MESSAGE_TYPE");
            Map(m => m.CLR_STATUS).Name("CLR_STATUS");
            Map(m => m.CONTRACT_TYPE).Name("CONTRACT_TYPE");
            Map(m => m.CARD_NUMBER).Name("CARD_NUMBER");
            Map(m => m.ACCOUNT_NUMBER).Name("ACCOUNT_NUMBER");
            Map(m => m.SENDER_ACCOUNT_NUMBER).Name("SENDER_ACCOUNT_NUMBER");
            Map(m => m.AUTH_CODE).Name("AUTH_CODE");
            Map(m => m.ARN).Name("ARN");
            Map(m => m.TRANS_DATE).Name("TRANS_DATE");
            Map(m => m.CLR_TXN_AMOUNT).Name("CLR_TXN_AMOUNT");
            Map(m => m.TXN_CURRENCY).Name("TXN_CURRENCY");
            Map(m => m.BILL_AMT).Name("BILL_AMT");
            Map(m => m.ACCT_CURR).Name("ACCT_CURR");
            Map(m => m.STTL_AMOUNT).Name("STTL_AMOUNT");
            Map(m => m.ST_REV).Name("ST_REV");
            Map(m => m.MATCH_STATUS).Name("MATCH_STATUS");
            Map(m => m.AUTH_ID).Name("AUTH_ID");
            Map(m => m.MCC).Name("MCC");
            Map(m => m.MERCHANT_NUMBER).Name("MERCHANT_NUMBER");
            Map(m => m.TERMINAL_NUMBER).Name("TERMINAL_NUMBER");
            Map(m => m.MERCHANT_NAME).Name("MERCHANT_NAME");
            Map(m => m.MERCHANT_CITY).Name("MERCHANT_CITY");
            Map(m => m.MERCHANT_COUNTRY).Name("MERCHANT_COUNTRY");
            Map(m => m.AUTH_OPR_ID).Name("AUTH_OPR_ID");
            Map(m => m.BASE_II_ID).Name("BASE_II_ID");
            Map(m => m.TRANSACTION_DATE).Name("TRANSACTION_DATE");
            Map(m => m.AUTH_CARD_NUMBER).Name("AUTH_CARD_NUMBER");
            Map(m => m.REVERSAL_FLAG).Name("REVERSAL_FLAG");
            Map(m => m.TXN_AMOUNT).Name("TXN_AMOUNT");
            Map(m => m.AUTH_CURRENCY).Name("AUTH_CURRENCY");
            Map(m => m.BILLING_AMOUNT).Name("BILLING_AMOUNT");
            Map(m => m.FEES).Name("FEES");
            Map(m => m.BILLING_CURRENCY).Name("BILLING_CURRENCY");
            Map(m => m.STATUS).Name("STATUS");
            Map(m => m.TXN_TYPE).Name("TXN_TYPE");
            Map(m => m.AUTH_MESSAGE_TYPE).Name("AUTH_MESSAGE_TYPE");
            Map(m => m.AUTH_MCC).Name("AUTH_MCC");
            Map(m => m.AUTH_MID).Name("AUTH_MID");
            Map(m => m.AUTH_MERCHANT_NAME).Name("AUTH_MERCHANT_NAME");
            Map(m => m.AUTH_CITY).Name("AUTH_CITY");
            Map(m => m.AUTH_COUNTRY).Name("AUTH_COUNTRY");
            Map(m => m.AUTH_TID).Name("AUTH_TID");
            Map(m => m.AUTH_ACCT_UMBER).Name("AUTH_ACCT_UMBER");
            Map(m => m.POS_COND_CODE).Name("POS_COND_CODE");
            Map(m => m.UTRNNO).Name("UTRNNO");
            Map(m => m.TRACE_NUMBER).Name("TRACE_NUMBER");
            Map(m => m.TRACE_TO_CBS).Name("TRACE_TO_CBS");
            Map(m => m.RRN).Name("RRN");
            Map(m => m.AUTH).Name("AUTH");
          
        }
    }
}