using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.GLTransaction;
using VISA_RECON.API.Application.Interfaces.Services;
using static VISA_RECON.API.Application.Constants.Constants;
namespace VISA_RECON.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GLController : ControllerBase
    {
        private readonly IGLTransactionService _glTransactionService;

        public GLController(
            IGLTransactionService glTransactionService)
        {
            _glTransactionService = glTransactionService;
        }

        [Tags("GL Data")]
        [HttpPost("uploadGLFiles")]
        [RequestSizeLimit(500 * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 500 * 1024 * 1024)]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Upload([FromForm] List<IFormFile> files)
        {
            if (files == null || files.Count == 0)
            {
                return BadRequest(new { Message = "No files were uploaded." });
            }


            var response =
                await _glTransactionService.ValidateAndMergeAsync(files);


            if (!response.IsSuccess)
            {
                return StatusCode(StatusCodes.Status400BadRequest, new
                {
                    Message = response.Message 
                });
            }

            return StatusCode(StatusCodes.Status200OK, new
            {
                Message = response.Message
            });
        }

        [Tags("GL Data")]
        [HttpPost("GetGLTransactionDetails")]
        public async Task<IActionResult> GetGLTransactionDetails(
                                                                  [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] GLTransactionRequest? request)
        {
            request ??= new GLTransactionRequest();

            var result = await _glTransactionService.GetGLTransactionDetailsAsync(request);

            if (!result.IsSuccess)
            {
                return BadRequest(new ApiResponse<PagedResponse<GLTransactionDetailsResponse>>(
                    result.Code,
                    result.Message,
                    result.Value));
            }

            return StatusCode(
                StatusCodes.Status200OK,
                new ApiResponse<PagedResponse<GLTransactionDetailsResponse>>(
                    result.Code,
                    result.Message,
                    result.Value));
        }
    }
}
