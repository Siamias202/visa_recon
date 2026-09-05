using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.Reconciliation;

namespace VISA_RECON.API.Application.Interfaces.Repository;

public interface IMatchingRepository
{
    Task<ReconciliationResultResponse> RunMatchingAsync();

    Task<PagedResponse<ReconciliationStoredResultResponse>>
        GetResultsAsync(ReconciliationResultsRequest request);

    Task<PagedResponse<ReconciliationStoredResultResponse>>
        GetDailyMatchesAsync(DailyMatchesRequest request);

    Task<MonthlyUnresolvedResponse> GetMonthlyUnresolvedAsync(
        MonthlyUnresolvedRequest request);

    Task<PagedResponse<IssuingReversalResponse>> GetReversalsAsync(
        IssuingReversalRequest request);
}
