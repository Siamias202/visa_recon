using Ekyc.Onboarding.API.Application.Common;
using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.GLTransaction;

namespace VISA_RECON.API.Application.Interfaces.Services
{
    public interface IGLTransactionService
    {
        Task<Result<Unit>> ValidateAndMergeAsync(List<IFormFile> files);

        Task<Result<PagedResponse<GLTransactionDetailsResponse>>> GetGLTransactionDetailsAsync(GLTransactionRequest request);

    }
}
