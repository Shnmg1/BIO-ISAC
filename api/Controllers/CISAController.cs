using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyApp.Namespace.Services;

namespace MyApp.Namespace
{
    [Route("api/[controller]")]
    [ApiController]
    public class CISAController : ControllerBase
    {
        private readonly CISAService _cisaService;

        public CISAController(CISAService cisaService)
        {
            _cisaService = cisaService;
        }

        [HttpGet("known-exploited")]
        public async Task<IActionResult> GetKnownExploitedVulnerabilities()
        {
            try
            {
                var data = await _cisaService.GetKnownExploitedVulnerabilities();
                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("cve/{cveId}")]
        public async Task<IActionResult> GetVulnerabilityByCVE(string cveId)
        {
            try
            {
                var vulnerability = await _cisaService.GetVulnerabilityByCVE(cveId);
                
                if (vulnerability == null)
                {
                    return NotFound(new { error = "Vulnerability not found in CISA catalog" });
                }
                
                return Ok(vulnerability);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("vendor/{vendor}")]
        public async Task<IActionResult> GetVulnerabilitiesByVendor(string vendor)
        {
            try
            {
                var vulnerabilities = await _cisaService.GetVulnerabilitiesByVendor(vendor);
                return Ok(vulnerabilities);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}

