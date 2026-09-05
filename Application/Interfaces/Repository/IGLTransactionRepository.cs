using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.GLTransaction;

namespace VISA_RECON.API.Application.Interfaces.Repositories
{
    public interface IGLTransactionRepository
    {
        Task<int> InsertBulkAsync(IEnumerable<UploadGLRequest> transactions);

        Task<PagedResponse<GLTransactionDetailsResponse>> GetGLTransactionDetailsListAsync(GLTransactionRequest request);
    }
}