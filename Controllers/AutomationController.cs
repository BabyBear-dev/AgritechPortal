using Microsoft.AspNetCore.Mvc;
using AgritechPortal.Data;
using AgritechPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace AgritechPortal.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AutomationController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<AutomationController> _logger;

    public AutomationController(AppDbContext db, ILogger<AutomationController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ============================================================
    // GET /api/automation/overview
    // ============================================================
    [HttpGet("overview")]
    public async Task<IActionResult> Overview()
    {
        try
        {
            var zones = await _db.Zones
                .Include(z => z.Actuators)
                .OrderBy(z => z.Id)
                .ToListAsync();

            var devices = await _db.SensorDevices
                .OrderBy(d => d.Id)
                .Select(d => new
                {
                    d.Id,
                    d.Name,
                    type = d.DeviceType,
                    d.IsActive,
                    d.LastSeen,
                    d.ZoneId,
                    status = d.LastSeen == null ? "never" :
                             (DateTime.UtcNow - d.LastSeen.Value).TotalMinutes < 5 ? "online" :
                             (DateTime.UtcNow - d.LastSeen.Value).TotalMinutes < 60 ? "idle" : "offline"
                })
                .ToListAsync();

            var result = zones.Select(z => new
            {
                zoneId = z.Id,
                zoneName = z.Name,
                crop = z.Crop,
                actuators = z.Actuators?.Select(a => new
                {
                    id = a.Id,
                    name = a.DeviceName,
                    type = a.Type,
                    isOn = a.IsOn,
                    lastChanged = a.LastChangedAt
                }).ToList() ?? new()
            }).ToList();

            return Ok(new
            {
                zones = result,
                devices,
                totalActuators = result.Sum(r => r.actuators.Count),
                activeCount = result.Sum(r => r.actuators.Count(a => a.isOn)),
                onlineDevices = devices.Count(d => d.status == "online")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automation overview failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ============================================================
    // POST /api/automation/actuator/{id}/toggle
    // ============================================================
    [HttpPost("actuator/{id}/toggle")]
    public async Task<IActionResult> ToggleActuator(int id)
    {
        try
        {
            var actuator = await _db.Actuators.FindAsync(id);
            if (actuator == null) return NotFound(new { error = "Actuator not found" });

            actuator.IsOn = !actuator.IsOn;
            actuator.LastChangedAt = DateTime.UtcNow;

            _db.ActionLogs.Add(new ActionLog
            {
                ActuatorId = actuator.Id,
                ZoneId = actuator.ZoneId,
                Action = actuator.IsOn ? "ON" : "OFF",
                Details = $"{actuator.DeviceName} turned {(actuator.IsOn ? "on" : "off")}",
                Source = "manual",
                Timestamp = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = $"{actuator.DeviceName} turned {(actuator.IsOn ? "ON" : "OFF")}",
                isOn = actuator.IsOn
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Toggle actuator failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ============================================================
    // POST /api/automation/actuator/{id}/set
    // ============================================================
    [HttpPost("actuator/{id}/set")]
    public async Task<IActionResult> SetActuator(int id, [FromBody] SetActuatorDto dto)
    {
        try
        {
            var actuator = await _db.Actuators.FindAsync(id);
            if (actuator == null) return NotFound(new { error = "Actuator not found" });

            actuator.IsOn = dto.State;
            actuator.LastChangedAt = DateTime.UtcNow;

            _db.ActionLogs.Add(new ActionLog
            {
                ActuatorId = actuator.Id,
                ZoneId = actuator.ZoneId,
                Action = dto.State ? "ON" : "OFF",
                Details = $"{actuator.DeviceName} set to {(dto.State ? "on" : "off")}",
                Source = dto.Source ?? "manual",
                Timestamp = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            return Ok(new { message = "State updated", isOn = actuator.IsOn });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ============================================================
    // GET /api/automation/logs?limit=50
    // ============================================================
    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs([FromQuery] int limit = 50)
    {
        try
        {
            var logs = await _db.ActionLogs
                .OrderByDescending(l => l.Timestamp)
                .Take(limit)
                .Select(l => new
                {
                    l.Id,
                    l.ActuatorId,
                    l.ZoneId,
                    l.Action,
                    l.Details,
                    l.Source,
                    l.Timestamp
                })
                .ToListAsync();

            return Ok(logs);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    public class SetActuatorDto
    {
        public bool State { get; set; }
        public string? Source { get; set; }
    }
}