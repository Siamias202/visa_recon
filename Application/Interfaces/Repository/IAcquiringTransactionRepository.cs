using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.AcquiringTransaction;
using VISA_RECON.API.Application.DTOs.GLTransaction;

namespace VISA_RECON.API.Application.Interfaces.Repository;

public interface IAcquiringTransactionRepository
{
    Task<int> InsertGlAsync(IEnumerable<UploadGLRequest> rows);
    Task<int> InsertFeAsync(IEnumerable<AcquiringFeTransaction> rows);
    Task<int> InsertEpAsync(IEnumerable<AcquiringEpTransaction> rows);
    Task<PagedResponse<GLTransactionDetailsResponse>> GetGlAsync(AcquiringDetailsRequest request);
    Task<PagedResponse<AcquiringFeTransaction>> GetFeAsync(AcquiringDetailsRequest request);
    Task<PagedResponse<AcquiringEpTransaction>> GetEpAsync(AcquiringDetailsRequest request);
}
