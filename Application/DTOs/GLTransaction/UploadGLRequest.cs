namespace VISA_RECON.API.Application.DTOs.GLTransaction
{
    public  class UploadGLRequest
    {
        public string AccountNo { get; set; } = string.Empty;

        public DateTime PostingDate { get; set; }

        public DateTime ValueDate { get; set; }

        public string BatchId { get; set; } = string.Empty;

        public string PostingBranch { get; set; } = string.Empty;

        public string UniqueReferenceNo { get; set; } = string.Empty;

        public string DebitCredit { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public string TransactionCode { get; set; } = string.Empty;

        public string TransactionName { get; set; } = string.Empty;

        public string Currency { get; set; } = string.Empty;

        public DateTime TimeStamp { get; set; }

        public string UniqueId { get; set; } = string.Empty;

        public string Narrative1 { get; set; } = string.Empty;

        public string Narrative2 { get; set; } = string.Empty;

        public string Narrative3 { get; set; } = string.Empty;

        public string Narrative4 { get; set; } = string.Empty;

        public string RRN { get; set; } = string.Empty;

        public string AuthCode { get; set; } = string.Empty;
    }


}