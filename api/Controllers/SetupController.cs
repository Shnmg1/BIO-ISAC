using Microsoft.AspNetCore.Mvc;
using MyApp.Namespace.Services;
using MySqlConnector;

namespace api.Controllers;

[ApiController]
[Route("api/setup")]
public class SetupController : ControllerBase
{
    private readonly DatabaseService _dbService;
    private readonly ILogger<SetupController> _logger;
    private readonly IWebHostEnvironment _environment;

    public SetupController(DatabaseService dbService, ILogger<SetupController> logger, IWebHostEnvironment environment)
    {
        _dbService = dbService;
        _logger = logger;
        _environment = environment;
    }

    [HttpGet("db")]
    public async Task<IActionResult> SetupDatabase()
    {
        try
        {
            var sqlFilePath = Path.Combine(_environment.ContentRootPath, "CreateTables.sql");
            if (!System.IO.File.Exists(sqlFilePath))
            {
                return NotFound(new { message = "CreateTables.sql not found" });
            }

            var sql = await System.IO.File.ReadAllTextAsync(sqlFilePath);

            // Split by command if necessary, but ExecuteAsync usually handles multiple statements if allowed by connection string.
            // However, MySqlConnector might need AllowUserVariables=True or similar for some scripts, but simple CREATE TABLEs are usually fine.
            // Better to execute one by one if they are separated by semi-colons?
            // For simplicity, let's try executing the whole block. If it fails, we might need to split.
            
            using var connection = await _dbService.GetConnectionAsync();
            using var command = new MySqlCommand(sql, connection);
            
            await command.ExecuteNonQueryAsync();

            return Ok(new { message = "Database tables created successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting up database");
            return StatusCode(500, new { message = "Database setup failed", error = ex.Message });
        }
    }
}
