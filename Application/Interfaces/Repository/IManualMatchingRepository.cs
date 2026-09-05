using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.Reconciliation;

namespace VISA_RECON.API.Application.Interfaces.Repository;

public interface IManualMatchingRepository
{
    Task<ManualMatchRequestResponse> CreateAsync(CreateManualMatchRequest request);
    Task<ManualMatchConfirmationResponse> ApproveAsync(long requestId, ReviewManualMatchRequest request);
    Task<ManualMatchRequestResponse> RejectAsync(long requestId, ReviewManualMatchRequest request);
    Task<PagedResponse<ManualMatchRequestResponse>> GetAsync(ManualMatchListRequest request);
}
