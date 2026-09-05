using VISA_RECON.API.Application.Common;

namespace VISA_RECON.API.Application.DTOs.AcquiringTransaction;

public sealed class AcquiringReconciliationRunResponse
{
    public long RunId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public string Status { get; set; } = "COMPLETED";
    public int MatchedCount { get; set; }
    public int MissingInCbsCount { get; set; }
    public int MissingInBoCount { get; set; }
    public int ReversalCount { get; set; }
}

public sealed class AcquiringReconciliationResultsRequest
{
    public long RunId { get; set; }
    public string? ReconciliationStatus { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class AcquiringReconciliationResultResponse
{
    public long Id { get; set; }
    public long RunId { get; set; }
    public string ReconciliationStatus { get; set; } = string.Empty;
    public DateTime? BusinessDate { get; set; }
    public long? GlTransactionId { get; set; }
    public long? EpTransactionId { get; set; }
    public long? FeTransactionId { get; set; }
    public string? Rrn { get; set; }
    public string? GlAuthCode { get; set; }
    public string? GlUniqueReferenceNo { get; set; }
    public decimal? GlAmount { get; set; }
    public string? FeReferenceNum { get; set; }
    public string? FeAuthCode { get; set; }
    public string? FeUtrNo { get; set; }
    public decimal? FeRequestAmount { get; set; }
    public string? MismatchReason { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class AcquiringReversalRequest
{
    public long RunId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class AcquiringReversalResponse
{
    public long Id { get; set; }
    public long RunId { get; set; }
    public string ReferenceNum { get; set; } = string.Empty;
    public string AuthCode { get; set; } = string.Empty;
    public long OriginalFeTransactionId { get; set; }
    public long ReversalFeTransactionId { get; set; }
    public decimal? OriginalRequestAmount { get; set; }
    public decimal? ReversalRequestAmount { get; set; }
    public DateTime CreatedAt { get; set; }
}

