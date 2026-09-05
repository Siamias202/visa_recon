using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.AcquiringTransaction;
using VISA_RECON.API.Application.Interfaces.Repository;
using VISA_RECON.API.Application.Interfaces.Services;
using static VISA_RECON.API.Application.Constants.Constants;

namespace VISA_RECON.API.Application.Services;

public sealed class AcquiringReconciliationService : IAcquiringReconciliationService
{
    private readonly IAcquiringReconciliationRepository _repository;

    public AcquiringReconciliationService(IAcquiringReconciliationRepository repository) =>
        _repository = repository;

    public async Task<Result<AcquiringReconciliationRunResponse>> RunAsync()
    {
        try
        {
            return Result<AcquiringReconciliationRunResponse>.Success(
                APIResponseCodes.SUCCESS_CODE,
                "Acquiring reconciliation completed successfully.",
                await _repository.RunAsync());
        }
        catch (Exception ex)
        {
            return Result<AcquiringReconciliationRunResponse>.Failure(
                APIResponseCodes.ERROR_CODE,
                $"Acquiring reconciliation failed: {ex.Message}");
        }
    }

    public async Task<Result<PagedResponse<AcquiringReconciliationResultResponse>>> GetResultsAsync(
        AcquiringReconciliationResultsRequest request)
    {
        try
        {
            return Result<PagedResponse<AcquiringReconciliationResultResponse>>.Success(
                APIResponseCodes.SUCCESS_CODE,
                "Acquiring reconciliation results retrieved successfully.",
                await _repository.GetResultsAsync(request));
        }
        catch (Exception ex)
        {
            return Result<PagedResponse<AcquiringReconciliationResultResponse>>.Failure(
                APIResponseCodes.ERROR_CODE,
                $"Failed to retrieve acquiring reconciliation results: {ex.Message}");
        }
    }

    public async Task<Result<PagedResponse<AcquiringReversalResponse>>> GetReversalsAsync(
        AcquiringReversalRequest request)
    {
        try
        {
            return Result<PagedResponse<AcquiringReversalResponse>>.Success(
                APIResponseCodes.SUCCESS_CODE,
                "Acquiring reversals retrieved successfully.",
                await _repository.GetReversalsAsync(request));
        }
        catch (Exception ex)
        {
            return Result<PagedResponse<AcquiringReversalResponse>>.Failure(
                APIResponseCodes.ERROR_CODE,
                $"Failed to retrieve acquiring reversals: {ex.Message}");
        }
    }
}

