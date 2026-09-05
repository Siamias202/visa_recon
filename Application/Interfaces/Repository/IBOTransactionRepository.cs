using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.BOTransaction;
using VISA_RECON.API.Application.DTOs.GLTransaction;

namespace VISA_RECON.API.Application.Interfaces.Repository
{
    public interface IBOTransactionRepository
    {
        Task<int> InsertBulkAsync(IEnumerable<UploadBORequest> transactions);

        Task<PagedResponse<BOTransactionDetailsResponse>> GetBOTransactionDetailsListAsync(BOTransactionRequest request);

    }
}
