namespace VISA_RECON.API.Application.DTOs.GLTransaction
{
    public sealed class GLTransactionDetailsResponse
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public long Id { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public DateTime? ReconciliationBusinessDate { get; set; }

        public string AccountNo { get; set; } = string.Empty;

        public string PostingDate { get; set; } = string.Empty;

        public string ValueDate { get; set; } = string.Empty;

        public string BatchId { get; set; } = string.Empty;

        public string PostingBranch { get; set; } = string.Empty;

        public string UniqueReferenceNo { get; set; } = string.Empty;

        public string DebitCredit { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public string TransactionCode { get; set; } = string.Empty;

        public string TransactionName { get; set; } = string.Empty;

        public string Currency { get; set; } = string.Empty;

        public string TimeStamp { get; set; } = string.Empty;

        public string UniqueId { get; set; } = string.Empty;

        public string Narrative1 { get; set; } = string.Empty;

        public string Narrative2 { get; set; } = string.Empty;

        public string Narrative3 { get; set; } = string.Empty;

        public string Narrative4 { get; set; } = string.Empty;

        public string RRN { get; set; } = string.Empty;

        public string AuthCode { get; set; } = string.Empty;
    }

}
