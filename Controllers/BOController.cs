using Microsoft.AspNetCore.Mvc;
using VISA_RECON.API.Application.Interfaces.Services;

namespace VISA_RECON.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BOController : ControllerBase
    {
        private readonly IBOTransactionService _boTransactionService;

        public BOController(IBOTransactionService boTransactionService)
        {
            _boTransactionService = boTransactionService;
        }

        [Tags("Back Office")]
        [HttpPost("upload")]
        [RequestSizeLimit(500 * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 500 * 1024 * 1024)]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Upload([FromForm] List<IFormFile> files)
        {
            if (files == null || files.Count == 0)
            {
                return BadRequest(new
                {
                    Code = "GL1001",
                    Message = "No CSV files uploaded."
                });
            }

            var response = await _boTransactionService.ValidateAndMergeAsync(files);

            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }
    }
}
