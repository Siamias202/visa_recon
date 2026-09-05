using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.AcquiringTransaction;
using VISA_RECON.API.Application.Interfaces.Services;

namespace VISA_RECON.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AcquiringController : ControllerBase
{
    private readonly IAcquiringTransactionService _service;
    private readonly IAcquiringReconciliationService _reconciliationService;

    public AcquiringController(
        IAcquiringTransactionService service,
        IAcquiringReconciliationService reconciliationService)
    {
        _service = service;
        _reconciliationService = reconciliationService;
    }

    [HttpPost("uploadGLFiles"), Consumes("multipart/form-data")]
    [RequestSizeLimit(500 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 500 * 1024 * 1024)]
    public Task<IActionResult> UploadGl([FromForm] List<IFormFile> files) => Upload(files, _service.UploadGlAsync);

    [HttpPost("uploadFEFiles"), Consumes("multipart/form-data")]
    [RequestSizeLimit(500 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 500 * 1024 * 1024)]
    public Task<IActionResult> UploadFe([FromForm] List<IFormFile> files) => Upload(files, _service.UploadFeAsync);

    [HttpPost("uploadEPFiles"), Consumes("multipart/form-data")]
    [RequestSizeLimit(500 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 500 * 1024 * 1024)]
    public Task<IActionResult> UploadEp([FromForm] List<IFormFile> files) => Upload(files, _service.UploadEpAsync);

    private async Task<IActionResult> Upload(List<IFormFile> files,
        Func<List<IFormFile>, Task<Result<Unit>>> action)
    {
        var result = await action(files);
        return result.IsSuccess ? Ok(new { result.Message }) : BadRequest(new { result.Message });
    }

    [HttpPost("GetGLTransactionDetails")]
    public async Task<IActionResult> GetGl([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AcquiringDetailsRequest? request)
    {
        var result = await _service.GetGlAsync(request ?? new());
        return result.IsSuccess ? Ok(new ApiResponse<object>(result.Code, result.Message, result.Value))
            : BadRequest(new { result.Message });
    }

    [HttpPost("GetFETransactionDetails")]
    public async Task<IActionResult> GetFe([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AcquiringDetailsRequest? request)
    {
        var result = await _service.GetFeAsync(request ?? new());
        return result.IsSuccess ? Ok(new ApiResponse<object>(result.Code, result.Message, result.Value))
            : BadRequest(new { result.Message });
    }

    [HttpPost("GetEPTransactionDetails")]
    public async Task<IActionResult> GetEp([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AcquiringDetailsRequest? request)
    {
        var result = await _service.GetEpAsync(request ?? new());
        return result.IsSuccess ? Ok(new ApiResponse<object>(result.Code, result.Message, result.Value))
            : BadRequest(new { result.Message });
    }

    [HttpPost("RunReconciliation")]
    public async Task<IActionResult> RunReconciliation()
    {
        var result = await _reconciliationService.RunAsync();
        return result.IsSuccess
            ? Ok(new ApiResponse<object>(result.Code, result.Message, result.Value))
            : BadRequest(new { result.Message });
    }

    [HttpPost("GetReconciliationResults")]
    public async Task<IActionResult> GetReconciliationResults(
        [FromBody] AcquiringReconciliationResultsRequest request)
    {
        var result = await _reconciliationService.GetResultsAsync(request);
        return result.IsSuccess
            ? Ok(new ApiResponse<object>(result.Code, result.Message, result.Value))
            : BadRequest(new { result.Message });
    }

    [HttpPost("GetReversals")]
    public async Task<IActionResult> GetReversals(
        [FromBody] AcquiringReversalRequest request)
    {
        var result = await _reconciliationService.GetReversalsAsync(request);
        return result.IsSuccess
            ? Ok(new ApiResponse<object>(result.Code, result.Message, result.Value))
            : BadRequest(new { result.Message });
    }
}
