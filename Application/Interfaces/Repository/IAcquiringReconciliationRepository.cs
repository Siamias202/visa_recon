using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.AcquiringTransaction;

namespace VISA_RECON.API.Application.Interfaces.Repository;

public interface IAcquiringReconciliationRepository
{
    Task<AcquiringReconciliationRunResponse> RunAsync();

    Task<PagedResponse<AcquiringReconciliationResultResponse>> GetResultsAsync(
        AcquiringReconciliationResultsRequest request);

    Task<PagedResponse<AcquiringReversalResponse>> GetReversalsAsync(
        AcquiringReversalRequest request);
}

