using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.Reconciliation;

namespace VISA_RECON.API.Application.Interfaces.Services;

public interface IMatchingService
{
    Task<Result<ReconciliationResultResponse>> RunMatchActionAsync();

    Task<Result<PagedResponse<ReconciliationStoredResultResponse>>>
        GetResultsAsync(ReconciliationResultsRequest request);

    Task<Result<PagedResponse<ReconciliationStoredResultResponse>>>
        GetDailyMatchesAsync(DailyMatchesRequest request);

    Task<Result<MonthlyUnresolvedResponse>> GetMonthlyUnresolvedAsync(
        MonthlyUnresolvedRequest request);

    Task<Result<PagedResponse<IssuingReversalResponse>>> GetReversalsAsync(
        IssuingReversalRequest request);
}
