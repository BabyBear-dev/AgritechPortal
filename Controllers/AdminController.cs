using Microsoft.AspNetCore.Mvc;
using AgritechPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace AgritechPortal.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<AdminController> _logger;

    public AdminController(AppDbContext db, ILogger<AdminController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ============================================================
    // GET /api/admin/status
    // System health + table row counts
    // ============================================================
    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        try
        {
            var counts = new Dictionary<string, int>
            {
                ["Operators"]    = await _db.Operators.CountAsync(),
                ["Farms"]        = await _db.Farms.CountAsync(),
                ["Greenhouses"]  = await _db.Greenhouses.CountAsync(),
                ["Zones"]        = await _db.Zones.CountAsync(),
                ["SensorReadings"] = await _db.SensorReadings.CountAsync(),
                ["SensorDevices"]  = await _db.SensorDevices.CountAsync(),
                ["Actuators"]      = await _db.Actuators.CountAsync(),
                ["Recommendations"] = await _db.Recommendations.CountAsync(),
                ["ChatMessages"]    = await _db.ChatMessages.CountAsync(),
                ["ActionLogs"]      = await _db.ActionLogs.CountAsync(),
                ["FarmMemories"]    = await _db.FarmMemories.CountAsync()
            };

            return Ok(new
            {
                timestamp = DateTime.UtcNow,
                environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
                version = "1.0.0",
                database = "connected",
                tables = counts
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admin status failed");
            return StatusCode(500, new { error = ex.Message, database = "error" });
        }
    }

    // ============================================================
    // GET /api/admin/operators
    // ============================================================
    [HttpGet("operators")]
    public async Task<IActionResult> Operators()
    {
        try
        {
            var ops = await _db.Operators
                .Select(o => new
                {
                    o.Id,
                    o.Email,
                    o.FullName,
                    o.Role,
                    o.CreatedAt
                })
                .ToListAsync();

            return Ok(ops);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ============================================================
    // DELETE /api/admin/chat?email=X
    // Wipe all chat for a user
    // ============================================================
    [HttpDelete("chat")]
    public async Task<IActionResult> WipeChat([FromQuery] string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { error = "email required" });

        try
        {
            var msgs = await _db.ChatMessages.Where(m => m.OperatorEmail == email).ToListAsync();
            _db.ChatMessages.RemoveRange(msgs);
            await _db.SaveChangesAsync();
            return Ok(new { message = $"Deleted {msgs.Count} messages" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}