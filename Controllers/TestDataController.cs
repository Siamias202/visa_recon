using Microsoft.AspNetCore.Mvc;
using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.Maintenance;
using VISA_RECON.API.Application.Interfaces.Repository;

namespace VISA_RECON.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class TestDataController : ControllerBase
{
    private readonly ITestDataResetRepository _repository;
    private readonly IWebHostEnvironment _environment;

    public TestDataController(
        ITestDataResetRepository repository,
        IWebHostEnvironment environment)
    {
        _repository = repository;
        _environment = environment;
    }

    [Tags("UAT Test Data")]
    [HttpDelete("DeleteIssuingData")]
    public Task<IActionResult> DeleteIssuingData(
        [FromBody] TestDataResetRequest request) =>
        DeleteAsync(
            request,
            "issuing",
            _repository.DeleteIssuingDataAsync);

    [Tags("UAT Test Data")]
    [HttpDelete("DeleteAcquiringData")]
    public Task<IActionResult> DeleteAcquiringData(
        [FromBody] TestDataResetRequest request) =>
        DeleteAsync(
            request,
            "acquiring",
            _repository.DeleteAcquiringDataAsync);

    private async Task<IActionResult> DeleteAsync(
        TestDataResetRequest request,
        string scope,
        Func<Task<TestDataResetResponse>> delete)
    {
        if (_environment.IsProduction())
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new ApiResponse<TestDataResetResponse>(
                    "403",
                    "Test-data deletion is disabled in Production.",
                    null));
        }

        if (!request.Confirm)
        {
            return BadRequest(new ApiResponse<TestDataResetResponse>(
                "400",
                $"Set confirm=true to delete all {scope} test data.",
                null));
        }

        try
        {
            var response = await delete();

            return Ok(new ApiResponse<TestDataResetResponse>(
                "200",
                $"All {scope} test data was truncated successfully.",
                response));
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiResponse<TestDataResetResponse>(
                "400",
                $"Failed to delete {scope} test data: {ex.Message}",
                null));
        }
    }
}
