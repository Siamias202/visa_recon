using VISA_RECON.API.Application.Common;

namespace VISA_RECON.API.Application.DTOs.Reconciliation;

public sealed class CreateManualMatchRequest
{
    public long CbsTransactionId { get; set; }
    public long BoTransactionId { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? EvidenceReference { get; set; }
}

public sealed class ReviewManualMatchRequest
{
    public string ReviewedBy { get; set; } = string.Empty;
    public string? ReviewNote { get; set; }
}

public sealed class ManualMatchListRequest
{
    public string? RequestStatus { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class ManualMatchRequestResponse
{
    public long Id { get; set; }
    public long CbsTransactionId { get; set; }
    public long BoTransactionId { get; set; }
    public string RequestStatus { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? EvidenceReference { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
    public long? ApprovedRunId { get; set; }
}

public sealed class ManualMatchConfirmationResponse
{
    public long RequestId { get; set; }
    public long RunId { get; set; }
    public long MatchId { get; set; }
    public long CbsTransactionId { get; set; }
    public long BoTransactionId { get; set; }
    public DateTime MatchedAt { get; set; }
}
