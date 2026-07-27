using System;

namespace VISA_RECON.API.Application.DTOs.GLTransaction
{
    public class UploadRequest
    {
        public string AccountNo { get; set; } = default!;
        public string PostingDate { get; set; } = default!;
        public string ValueDate { get; set; } = default!;
        public string BatchId { get; set; } = default!;
        public string PostingBranch { get; set; } = default!;
        public string UniqueReferenceNo { get; set; } = default!;
        public string DebitCredit { get; set; } = default!;
        public string Amount { get; set; } = default!;
        public string TransactionCode { get; set; } = default!;
        public string TransactionName { get; set; } = default!;
        public string Currency { get; set; } = default!;
        public string TimeStamp { get; set; } = default!;
        public string UniqueId { get; set; } = default!;
        public string Narrative1 { get; set; } = default!;
        public string Narrative2 { get; set; } = default!;
        public string RRN { get; set; } = default!;
        public string AuthCode { get; set; } = default!;
        public string Narrative3 { get; set; } = default!;
        public string Narrative4 { get; set; } = default!;
    }
}