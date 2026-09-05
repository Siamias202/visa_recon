using System.Text.Json;
using VISA_RECON.API.Application.Common;

namespace VISA_RECON.API.Application.DTOs.Reconciliation;

public sealed class ReconciliationResultsRequest
{
    public long RunId { get; set; }

    public string? ReconciliationStatus { get; set; }

    public string? Currency { get; set; }

    public string? Category { get; set; }

    public string? AccountNumber { get; set; }

    public DateTime? DateFrom { get; set; }

    public DateTime? DateTo { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

public sealed class ReconciliationStoredResultResponse
{
    public long Id { get; set; }

    public long RunId { get; set; }

    public string ReconciliationStatus { get; set; } = string.Empty;

    public DateTime? BusinessDate { get; set; }

    public string? AgeBucket { get; set; }

    public JsonElement? CbsData { get; set; }

    public JsonElement? BoData { get; set; }

    public DateTime CreatedAt { get; set; }
}

public sealed class MonthlyUnresolvedRequest
{
    // Zero uses the latest completed reconciliation run.
    public long RunId { get; set; }

    public DateTime? AsOfDate { get; set; }

    public string? ReconciliationStatus { get; set; }

    public string? AgeBucket { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

public sealed class MonthlyUnresolvedResponse
{
    public long RunId { get; set; }

    public DateTime AsOfDate { get; set; }

    public List<AgeBucketSummaryResponse> Summary { get; set; } = [];

    public PagedResponse<ReconciliationStoredResultResponse> Results { get; set; } = new();
}

public sealed class AgeBucketSummaryResponse
{
    public string ReconciliationStatus { get; set; } = string.Empty;

    public string AgeBucket { get; set; } = string.Empty;

    public int ItemCount { get; set; }
}

public sealed class IssuingReversalRequest
{
    public long RunId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class IssuingReversalResponse
{
    public long Id { get; set; }
    public long RunId { get; set; }
    public string Utrnno { get; set; } = string.Empty;
    public string AuthCode { get; set; } = string.Empty;
    public long OriginalBoTransactionId { get; set; }
    public long ReversalBoTransactionId { get; set; }
    public decimal? OriginalSettlementAmount { get; set; }
    public decimal? ReversalSettlementAmount { get; set; }
    public DateTime CreatedAt { get; set; }
}
