using VISA_RECON.API.Application.DTOs.Maintenance;

namespace VISA_RECON.API.Application.Interfaces.Repository;

public interface ITestDataResetRepository
{
    Task<TestDataResetResponse> DeleteIssuingDataAsync();
    Task<TestDataResetResponse> DeleteAcquiringDataAsync();
}
