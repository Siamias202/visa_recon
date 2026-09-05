using Microsoft.AspNetCore.Mvc;
using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.Reconciliation;
using VISA_RECON.API.Application.Interfaces.Services;

namespace VISA_RECON.API.Controllers;

[ApiController]
[Route("api/issuing/manual-matches")]
[Tags("Issuing Manual Match")]
public sealed class IssuingManualMatchController : ControllerBase
{
    private readonly IManualMatchingService _service;

    public IssuingManualMatchController(IManualMatchingService service) =>
        _service = service;

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateManualMatchRequest request)
    {
        var result = await _service.CreateAsync(request);
        var response = new ApiResponse<ManualMatchRequestResponse>(
            result.Code, result.Message, result.Value);
        return result.IsSuccess ? Ok(response) : BadRequest(response);
    }

    [HttpPost("{requestId:long}/approve")]
    public async Task<IActionResult> Approve(
        long requestId, [FromBody] ReviewManualMatchRequest request)
    {
        var result = await _service.ApproveAsync(requestId, request);
        var response = new ApiResponse<ManualMatchConfirmationResponse>(
            result.Code, result.Message, result.Value);
        return result.IsSuccess ? Ok(response) : BadRequest(response);
    }

    [HttpPost("{requestId:long}/reject")]
    public async Task<IActionResult> Reject(
        long requestId, [FromBody] ReviewManualMatchRequest request)
    {
        var result = await _service.RejectAsync(requestId, request);
        var response = new ApiResponse<ManualMatchRequestResponse>(
            result.Code, result.Message, result.Value);
        return result.IsSuccess ? Ok(response) : BadRequest(response);
    }

    [HttpPost("search")]
    public async Task<IActionResult> Get([FromBody] ManualMatchListRequest request)
    {
        var result = await _service.GetAsync(request);
        var response = new ApiResponse<PagedResponse<ManualMatchRequestResponse>>(
            result.Code, result.Message, result.Value);
        return result.IsSuccess ? Ok(response) : BadRequest(response);
    }
}
