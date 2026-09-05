using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.BOTransaction;

namespace VISA_RECON.API.Application.Interfaces.Services
{
    public interface IBOTransactionService
    {
        Task<Result<Unit>> ValidateAndMergeAsync(List<IFormFile> files);

        Task<Result<PagedResponse<BOTransactionDetailsResponse>>> GetBOTransactionsListAsync(BOTransactionRequest request);
    }
}
