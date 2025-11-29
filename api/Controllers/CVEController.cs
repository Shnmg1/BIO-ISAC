using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyApp.Namespace.Services;

namespace MyApp.Namespace
{
    [Route("api/[controller]")]
    [ApiController]
    public class CVEController : ControllerBase
    {
        private readonly NISTService _nistService;

        public CVEController(NISTService nistService)
        {
            _nistService = nistService;
        }

        [HttpGet("{cveId}")]
        public async Task<IActionResult> GetCVE(string cveId)
        {
            try
            {
                var cve = await _nistService.GetCVE(cveId);
                return Ok(cve);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetCVEs([FromQuery] int resultsPerPage = 20, [FromQuery] int startIndex = 0, [FromQuery] string? keywordSearch = null, [FromQuery] string? pubStartDate = null, [FromQuery] string? pubEndDate = null)
        {
            try
            {
                var cves = await _nistService.GetCVEs(resultsPerPage, startIndex, keywordSearch, pubStartDate, pubEndDate);
                return Ok(cves);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("cpe/{cpeName}")]
        public async Task<IActionResult> GetCPEMatch(string cpeName)
        {
            try
            {
                var match = await _nistService.GetCPEMatch(cpeName);
                return Ok(match);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("test-connection")]
        public async Task<IActionResult> TestConnection()
        {
            try
            {
                var result = await _nistService.TestNVDConnection();
                if (result.Success)
                {
                    return Ok(new { 
                        success = true, 
                        message = "NVD API connection successful",
                        data = result.Data,
                        isAvailable = _nistService.IsAvailable
                    });
                }
                else
                {
                    return StatusCode(503, new { 
                        success = false, 
                        error = result.Error,
                        isAvailable = _nistService.IsAvailable
                    });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}

