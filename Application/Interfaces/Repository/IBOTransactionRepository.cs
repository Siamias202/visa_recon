using VISA_RECON.API.Application.DTOs.BOTransaction;

namespace VISA_RECON.API.Application.Interfaces.Repository
{
    public interface IBOTransactionRepository
    {
        Task<int> InsertBulkAsync(IEnumerable<UploadBORequest> transactions);

    }
}
