using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.Reconciliation;
using VISA_RECON.API.Application.Interfaces.Repository;
using VISA_RECON.API.Application.Interfaces.Services;
using static VISA_RECON.API.Application.Constants.Constants;

namespace VISA_RECON.API.Application.Services;

public sealed class MatchingService : IMatchingService
{
    private readonly IMatchingRepository _repository;

    public MatchingService(IMatchingRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<ReconciliationResultResponse>> RunMatchActionAsync()
    {
        try
        {
            var response = await _repository.RunMatchingAsync();

            return Result<ReconciliationResultResponse>.Success(
                APIResponseCodes.SUCCESS_CODE,
                "CBS and BO reconciliation completed successfully.",
                response);
        }
        catch (Exception ex)
        {
            return Result<ReconciliationResultResponse>.Failure(
                APIResponseCodes.ERROR_CODE,
                $"CBS and BO reconciliation failed: {ex.Message}");
        }
    }

    public async Task<Result<PagedResponse<ReconciliationStoredResultResponse>>>
        GetResultsAsync(ReconciliationResultsRequest request)
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

    public async Task<Result<PagedResponse<ReconciliationStoredResultResponse>>>
        GetDailyMatchesAsync(DailyMatchesRequest request)
    {
        try
        {
            var response = await _repository.GetDailyMatchesAsync(request);
            return Result<PagedResponse<ReconciliationStoredResultResponse>>.Success(
                APIResponseCodes.SUCCESS_CODE,
                "Daily issuing matches retrieved successfully.", response);
        }
        catch (Exception ex)
        {
            return Result<PagedResponse<ReconciliationStoredResultResponse>>.Failure(
                APIResponseCodes.ERROR_CODE,
                $"Failed to retrieve daily issuing matches: {ex.Message}");
        }
    }

    public async Task<Result<PagedResponse<IssuingReversalResponse>>> GetReversalsAsync(
        IssuingReversalRequest request)
    {
        try
        {
            return Result<PagedResponse<IssuingReversalResponse>>.Success(
                APIResponseCodes.SUCCESS_CODE,
                "Issuing reversals retrieved successfully.",
                await _repository.GetReversalsAsync(request));
        }
        catch (Exception ex)
        {
            return Result<PagedResponse<IssuingReversalResponse>>.Failure(
                APIResponseCodes.ERROR_CODE,
                $"Failed to retrieve issuing reversals: {ex.Message}");
        }
    }
}
