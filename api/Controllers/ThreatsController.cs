using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyApp.Namespace.Services;

namespace MyApp.Namespace
{
    [Route("api/[controller]")]
    [ApiController]
    public class ThreatsController : ControllerBase
    {
        private readonly OTXService _otxService;

        public ThreatsController(OTXService otxService)
        {
            _otxService = otxService;
        }

        [HttpGet("hash/{hash}")]
        public async Task<IActionResult> GetByHash(string hash)
        {
            try
            {
                var threat = await _otxService.GetThreatByHash(hash);
                return Ok(threat);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("hash/{hash}/pulses")]
        public async Task<IActionResult> GetPulses(string hash)
        {
            try
            {
                var pulses = await _otxService.GetThreatPulses(hash);
                return Ok(pulses);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("hash/{hash}/analysis")]
        public async Task<IActionResult> GetAnalysis(string hash)
        {
            try
            {
                var analysis = await _otxService.GetThreatAnalysis(hash);
                return Ok(analysis);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}

