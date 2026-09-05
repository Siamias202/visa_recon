using CsvHelper.Configuration;
using VISA_RECON.API.Application.DTOs.BOTransaction;

namespace VISA_RECON.API.Application.Mappings.BOMappingsHelper
{
    public sealed class UploadBORequestMappings
        : ClassMap<UploadBORequest>
    {
        public UploadBORequestMappings()
        {
            Map(x => x.SESSION_ID)
                .Name("SESSION_ID");

            Map(x => x.BO_OPER_ID)
                .Name("BO_OPER_ID");

            Map(x => x.EP_STTL_DATE)
                .Name("EP_STTL_DATE");

            Map(x => x.RUN_DATE)
                .Name("RUN_DATE");

            Map(x => x.TRX_TYPE)
                .Name("TRX_TYPE");

            Map(x => x.MESSAGE_TYPE)
                .Name("MESSAGE_TYPE");

            Map(x => x.CLR_STATUS)
                .Name("CLR_STATUS");

            Map(x => x.CONTRACT_TYPE)
                .Name("CONTRACT_TYPE");

            Map(x => x.CARD_NUMBER)
                .Name("CARD_NUMBER");

            Map(x => x.ACCOUNT_NUMBER)
                .Name("ACCOUNT_NUMBER");

            Map(x => x.SENDER_ACCOUNT_NUMBER)
                .Name("SENDER_ACCOUNT_NUMBER");

            Map(x => x.AUTH_CODE)
                .Name("AUTH_CODE");

            Map(x => x.ARN)
                .Name("ARN");

            Map(x => x.TRANS_DATE)
                .Name("TRANS_DATE");

            Map(x => x.CLR_TXN_AMOUNT)
                .Name("CLR_TXN_AMOUNT");

            Map(x => x.TXN_CURRENCY)
                .Name("TXN_CURRENCY");

            Map(x => x.BILL_AMT)
                .Name("BILL_AMT");

            Map(x => x.ACCT_CURR)
                .Name("ACCT_CURR");

            Map(x => x.STTL_AMOUNT)
                .Name("STTL_AMOUNT");

            Map(x => x.ST_REV)
                .Name("ST_REV");

            Map(x => x.MATCH_STATUS)
                .Name("MATCH_STATUS");

            Map(x => x.AUTH_ID)
                .Name("AUTH_ID");

            Map(x => x.MCC)
                .Name("MCC");

            Map(x => x.MERCHANT_NUMBER)
                .Name("MERCHANT_NUMBER");

            Map(x => x.TERMINAL_NUMBER)
                .Name("TERMINAL_NUMBER");

            Map(x => x.MERCHANT_NAME)
                .Name("MERCHANT_NAME");

            Map(x => x.MERCHANT_CITY)
                .Name("MERCHANT_CITY");

            Map(x => x.MERCHANT_COUNTRY)
                .Name("MERCHANT_COUNTRY");

            Map(x => x.AUTH_OPR_ID)
                .Name("AUTH_OPR_ID");

            Map(x => x.BASE_II_ID)
                .Name("BASE_II_ID");

            Map(x => x.TRANSACTION_DATE)
                .Name("TRANSACTION_DATE");

            Map(x => x.AUTH_CARD_NUMBER)
                .Name("AUTH_CARD_NUMBER");

            Map(x => x.REVERSAL_FLAG)
                .Name("REVERSAL_FLAG");

            Map(x => x.TXN_AMOUNT)
                .Name("TXN_AMOUNT");

            Map(x => x.AUTH_CURRENCY)
                .Name("AUTH_CURRENCY");

            Map(x => x.BILLING_AMOUNT)
                .Name("BILLING_AMOUNT");

            Map(x => x.FEES)
                .Name("FEES");

            Map(x => x.BILLING_CURRENCY)
                .Name("BILLING_CURRENCY");

            Map(x => x.STATUS)
                .Name("STATUS");

            Map(x => x.TXN_TYPE)
                .Name("TXN_TYPE");

            Map(x => x.AUTH_MESSAGE_TYPE)
                .Name("AUTH_MESSAGE_TYPE");

            Map(x => x.AUTH_MCC)
                .Name("AUTH_MCC");

            Map(x => x.AUTH_MID)
                .Name("AUTH_MID");

            Map(x => x.AUTH_MERCHANT_NAME)
                .Name("AUTH_MERCHANT_NAME");

            Map(x => x.AUTH_CITY)
                .Name("AUTH_CITY");

            Map(x => x.AUTH_COUNTRY)
                .Name("AUTH_COUNTRY");

            Map(x => x.AUTH_TID)
                .Name("AUTH_TID");

            Map(x => x.AUTH_ACCT_UMBER)
                .Name("AUTH_ACCT_UMBER");

            Map(x => x.POS_COND_CODE)
                .Name("POS_COND_CODE");

            Map(x => x.UTRNNO)
                .Name("UTRNNO");

            Map(x => x.TRACE_NUMBER)
                .Name("TRACE_NUMBER");

            Map(x => x.TRACE_TO_CBS)
                .Name("TRACE_TO_CBS");

            Map(x => x.RRN)
                .Name("RRN");

            Map(x => x.AUTH)
                .Name("AUTH");

          
        }
    }
}
