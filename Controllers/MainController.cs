using Microsoft.AspNetCore.Mvc;
using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.Reconciliation;
using VISA_RECON.API.Application.Interfaces.Services;

namespace VISA_RECON.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MainController : ControllerBase
{
    private readonly IMatchingService _matchingService;

    public MainController(IMatchingService matchingService)
    {
        _matchingService = matchingService;
    }

    [Tags("MatchController")]
    [HttpPost("RunMatchAction")]
    public async Task<IActionResult> RunMatchAction()
    {
        var result = await _matchingService.RunMatchActionAsync();

        if (!result.IsSuccess)
        {
            return BadRequest(new ApiResponse<ReconciliationResultResponse>(
                result.Code,
                result.Message,
                result.Value));
        }

        return StatusCode(StatusCodes.Status200OK, new ApiResponse<ReconciliationResultResponse>(
            result.Code,
            result.Message,
            result.Value));
    }

    [Tags("MatchController")]
    [HttpPost("GetMatchingResults")]
    public async Task<IActionResult> GetMatchingResults(
        [FromBody] ReconciliationResultsRequest request)
    {
        var result = await _matchingService.GetResultsAsync(request);

        if (!result.IsSuccess)
        {
            return BadRequest(new ApiResponse<
                PagedResponse<ReconciliationStoredResultResponse>>(
                result.Code,
                result.Message,
                result.Value));
        }

        return Ok(new ApiResponse<
            PagedResponse<ReconciliationStoredResultResponse>>(
            result.Code,
            result.Message,
            result.Value));
    }

    [Tags("MatchController")]
    [HttpPost("GetMonthlyUnresolvedItems")]
    public async Task<IActionResult> GetMonthlyUnresolvedItems(
        [FromBody] MonthlyUnresolvedRequest request)
    {
        var result = await _matchingService.GetMonthlyUnresolvedAsync(request);

        if (!result.IsSuccess)
        {
            return BadRequest(new ApiResponse<MonthlyUnresolvedResponse>(
                result.Code,
                result.Message,
                result.Value));
        }

        return Ok(new ApiResponse<MonthlyUnresolvedResponse>(
            result.Code,
            result.Message,
            result.Value));
    }

    [Tags("MatchController")]
    [HttpPost("GetReversals")]
    public async Task<IActionResult> GetReversals(
        [FromBody] IssuingReversalRequest request)
    {
        var result = await _matchingService.GetReversalsAsync(request);
        var response = new ApiResponse<PagedResponse<IssuingReversalResponse>>(
            result.Code,
            result.Message,
            result.Value);

        return result.IsSuccess ? Ok(response) : BadRequest(response);
    }
}
