namespace VISA_RECON.API.Application.DTOs.GLTransaction
{
    public class GLTransactionRequest
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? SearchQuery { get; set; }
        public string? Currency { get; set; }
        public string? Category { get; set; }
        public string? AccountNumber { get; set; }
        public string? SortBy { get; set; }
        public string? SortDirection { get; set; }
    }
}
