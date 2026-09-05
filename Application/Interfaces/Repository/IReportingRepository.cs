using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.Reconciliation;

namespace VISA_RECON.API.Application.Interfaces.Repository;

public interface IReportingRepository
{
    Task<PagedResponse<ReconciliationStoredResultResponse>> GetResultsAsync(
        ReconciliationResultsRequest request);

    Task<MonthlyUnresolvedResponse> GetMonthlyUnresolvedAsync(
        MonthlyUnresolvedRequest request);
}
