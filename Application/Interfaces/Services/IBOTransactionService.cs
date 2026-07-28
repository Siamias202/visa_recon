using VISA_RECON.API.Application.Common;

namespace VISA_RECON.API.Application.Interfaces.Services
{
    public interface IBOTransactionService
    {
        Task<Result<Unit>> ValidateAndMergeAsync(List<IFormFile> files);
    }
}
