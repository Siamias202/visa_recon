using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.Reconciliation;
using VISA_RECON.API.Application.Interfaces.Repository;
using VISA_RECON.API.Application.Interfaces.Services;
using static VISA_RECON.API.Application.Constants.Constants;

namespace VISA_RECON.API.Application.Services;

public sealed class ManualMatchingService : IManualMatchingService
{
    private readonly IManualMatchingRepository _repository;

    public ManualMatchingService(IManualMatchingRepository repository) =>
        _repository = repository;

    public Task<Result<ManualMatchRequestResponse>> CreateAsync(
        CreateManualMatchRequest request) => ExecuteAsync(
        () => _repository.CreateAsync(request),
        "Manual match request created successfully.",
        "Failed to create manual match request");

    public Task<Result<ManualMatchConfirmationResponse>> ApproveAsync(
        long requestId, ReviewManualMatchRequest request) => ExecuteAsync(
        () => _repository.ApproveAsync(requestId, request),
        "Manual match confirmed successfully.",
        "Failed to confirm manual match");

    public Task<Result<ManualMatchRequestResponse>> RejectAsync(
        long requestId, ReviewManualMatchRequest request) => ExecuteAsync(
        () => _repository.RejectAsync(requestId, request),
        "Manual match request rejected successfully.",
        "Failed to reject manual match request");

    public Task<Result<PagedResponse<ManualMatchRequestResponse>>> GetAsync(
        ManualMatchListRequest request) => ExecuteAsync(
        () => _repository.GetAsync(request),
        "Manual match requests retrieved successfully.",
        "Failed to retrieve manual match requests");

    private static async Task<Result<T>> ExecuteAsync<T>(
        Func<Task<T>> action, string successMessage, string failurePrefix)
    {
        try
        {
            return Result<T>.Success(
                APIResponseCodes.SUCCESS_CODE, successMessage, await action());
        }
        catch (Exception ex)
        {
            return Result<T>.Failure(
                APIResponseCodes.ERROR_CODE, $"{failurePrefix}: {ex.Message}");
        }
    }
}
