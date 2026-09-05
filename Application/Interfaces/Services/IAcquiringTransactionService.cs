using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.AcquiringTransaction;
using VISA_RECON.API.Application.DTOs.GLTransaction;

namespace VISA_RECON.API.Application.Interfaces.Services;

public interface IAcquiringTransactionService
{
    Task<Result<Unit>> UploadGlAsync(List<IFormFile> files);
    Task<Result<Unit>> UploadFeAsync(List<IFormFile> files);
    Task<Result<Unit>> UploadEpAsync(List<IFormFile> files);
    Task<Result<PagedResponse<GLTransactionDetailsResponse>>> GetGlAsync(AcquiringDetailsRequest request);
    Task<Result<PagedResponse<AcquiringFeTransaction>>> GetFeAsync(AcquiringDetailsRequest request);
    Task<Result<PagedResponse<AcquiringEpTransaction>>> GetEpAsync(AcquiringDetailsRequest request);
}
