using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.Reconciliation;
using VISA_RECON.API.Application.Interfaces.Repository;
using VISA_RECON.API.Application.Interfaces.Services;
using static VISA_RECON.API.Application.Constants.Constants;

namespace VISA_RECON.API.Application.Services;

public sealed class ReportingService : IReportingService
{
    private readonly IReportingRepository _repository;

    public ReportingService(IReportingRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<PagedResponse<ReconciliationStoredResultResponse>>> GetResultsAsync(
        ReconciliationResultsRequest request)
    {
        try
        {
            var response = await _repository.GetResultsAsync(request);
            return Result<PagedResponse<ReconciliationStoredResultResponse>>.Success(
                APIResponseCodes.SUCCESS_CODE,
                "Reconciliation results retrieved successfully.",
                response);
        }
        catch (Exception ex)
        {
            return Result<PagedResponse<ReconciliationStoredResultResponse>>.Failure(
                APIResponseCodes.ERROR_CODE,
                $"Failed to retrieve reconciliation results: {ex.Message}");
        }
    }

    public async Task<Result<MonthlyUnresolvedResponse>> GetMonthlyUnresolvedAsync(
        MonthlyUnresolvedRequest request)
    {
        try
        {
            var response = await _repository.GetMonthlyUnresolvedAsync(request);
            return Result<MonthlyUnresolvedResponse>.Success(
                APIResponseCodes.SUCCESS_CODE,
                "Monthly unresolved items retrieved successfully.",
                response);
        }
        catch (Exception ex)
        {
            return Result<MonthlyUnresolvedResponse>.Failure(
                APIResponseCodes.ERROR_CODE,
                $"Failed to retrieve monthly unresolved items: {ex.Message}");
        }
    }
}
