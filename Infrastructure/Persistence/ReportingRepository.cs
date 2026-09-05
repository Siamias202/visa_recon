using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.Reconciliation;
using VISA_RECON.API.Application.Interfaces.Repository;

namespace VISA_RECON.API.Infrastructure.Persistence;

// Both reporting route sets read the same ID-based result source.
public sealed class ReportingRepository : IReportingRepository
{
    private readonly IMatchingRepository _matchingRepository;

    public ReportingRepository(IMatchingRepository matchingRepository) =>
        _matchingRepository = matchingRepository;

    public Task<PagedResponse<ReconciliationStoredResultResponse>> GetResultsAsync(
        ReconciliationResultsRequest request) =>
        _matchingRepository.GetResultsAsync(request);

    public Task<MonthlyUnresolvedResponse> GetMonthlyUnresolvedAsync(
        MonthlyUnresolvedRequest request) =>
        _matchingRepository.GetMonthlyUnresolvedAsync(request);
}
