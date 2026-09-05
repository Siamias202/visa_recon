using VISA_RECON.API.Application.DTOs.BOTransaction;
using VISA_RECON.API.Application.DTOs.GLTransaction;

namespace VISA_RECON.API.Application.DTOs.Reconciliation;

public sealed class ReconciliationResultResponse
{
    public long RunId { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime CompletedAt { get; set; }

    public string Status { get; set; } = "COMPLETED";

    public int MatchedCount { get; set; }

    public int MissingInCbsCount { get; set; }

    public int MissingInBoCount { get; set; }

    public int ReverseCount { get; set; }

    public int ReverseTransactionsArchived { get; set; }
}

public sealed class MatchedTransactionResponse
{
    public GLTransactionDetailsResponse CbsData { get; set; } = new();

    public BOTransactionDetailsResponse BoData { get; set; } = new();
}
