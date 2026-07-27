using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.GLTransaction;

namespace VISA_RECON.API.Application.Interfaces.Services
{
    public interface IGLTransactionService
    {
        Task<bool> ValidateAndMergeAsync(List<IFormFile> files);

    }
}
