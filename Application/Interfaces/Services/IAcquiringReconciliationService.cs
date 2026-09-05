using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.AcquiringTransaction;

namespace VISA_RECON.API.Application.Interfaces.Services;

public interface IAcquiringReconciliationService
{
    Task<Result<AcquiringReconciliationRunResponse>> RunAsync();

    Task<Result<PagedResponse<AcquiringReconciliationResultResponse>>> GetResultsAsync(
        AcquiringReconciliationResultsRequest request);

    Task<Result<PagedResponse<AcquiringReversalResponse>>> GetReversalsAsync(
        AcquiringReversalRequest request);
}

