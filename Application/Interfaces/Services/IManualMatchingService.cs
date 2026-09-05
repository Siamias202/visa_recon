using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.Reconciliation;

namespace VISA_RECON.API.Application.Interfaces.Services;

public interface IManualMatchingService
{
    Task<Result<ManualMatchRequestResponse>> CreateAsync(CreateManualMatchRequest request);
    Task<Result<ManualMatchConfirmationResponse>> ApproveAsync(long requestId, ReviewManualMatchRequest request);
    Task<Result<ManualMatchRequestResponse>> RejectAsync(long requestId, ReviewManualMatchRequest request);
    Task<Result<PagedResponse<ManualMatchRequestResponse>>> GetAsync(ManualMatchListRequest request);
}
