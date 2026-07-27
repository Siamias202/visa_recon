using Microsoft.AspNetCore.Mvc;
using VISA_RECON.API.Application.Interfaces.Services;

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


        [HttpPost("upload")]
        [RequestSizeLimit(500 * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 500 * 1024 * 1024)]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Upload(
            [FromForm] List<IFormFile> files)
        {
            if (files == null || files.Count == 0)
            {
                return BadRequest(new
                {
                    Code = "400",
                    Message = "No CSV files uploaded."
                });
            }


            var response =
                await _glTransactionService.ValidateAndMergeAsync(files);


            if (!response)
            {
                return BadRequest(response);
            }


            return Ok(response);
        }
    }
}