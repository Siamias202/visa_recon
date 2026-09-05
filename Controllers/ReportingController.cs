using Microsoft.AspNetCore.Mvc;
using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.Reconciliation;
using VISA_RECON.API.Application.Interfaces.Services;

namespace VISA_RECON.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ReportingController : ControllerBase
{
    private readonly IReportingService _reportingService;

    public ReportingController(IReportingService reportingService)
    {
        _reportingService = reportingService;
    }

    [HttpPost("GetMatchingResults")]
    public async Task<IActionResult> GetMatchingResults(
        [FromBody] ReconciliationResultsRequest request)
    {
        var result = await _reportingService.GetResultsAsync(request);
        var response = new ApiResponse<PagedResponse<ReconciliationStoredResultResponse>>(
            result.Code, result.Message, result.Value);

        return result.IsSuccess ? Ok(response) : BadRequest(response);
    }

    [HttpPost("GetMonthlyUnresolvedItems")]
    public async Task<IActionResult> GetMonthlyUnresolvedItems(
        [FromBody] MonthlyUnresolvedRequest request)
    {
        var result = await _reportingService.GetMonthlyUnresolvedAsync(request);
        var response = new ApiResponse<MonthlyUnresolvedResponse>(
            result.Code, result.Message, result.Value);

        return result.IsSuccess ? Ok(response) : BadRequest(response);
    }
}
