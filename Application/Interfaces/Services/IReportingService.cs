using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.Reconciliation;

namespace VISA_RECON.API.Application.Interfaces.Services;

public interface IReportingService
{
    Task<Result<PagedResponse<ReconciliationStoredResultResponse>>> GetResultsAsync(
        ReconciliationResultsRequest request);

    Task<Result<MonthlyUnresolvedResponse>> GetMonthlyUnresolvedAsync(
        MonthlyUnresolvedRequest request);
}
