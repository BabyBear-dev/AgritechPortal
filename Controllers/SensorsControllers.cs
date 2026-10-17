using Microsoft.AspNetCore.Mvc;
using AgritechPortal.Models;
using AgritechPortal.Services;

namespace AgritechPortal.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SensorController : ControllerBase
{
    private readonly DataIngestionService _ingestion;
    private readonly ILogger<SensorController> _logger;

    public SensorController(DataIngestionService ingestion, ILogger<SensorController> logger)
    {
        _ingestion = ingestion;
        _logger = logger;
    }

    // POST: /api/sensor/register
    [HttpPost("register")]
    public async Task<IActionResult> RegisterDevice([FromBody] RegisterDeviceDto dto)
    {
        try
        {
            var device = await _ingestion.RegisterDeviceAsync(dto.ZoneId, dto.Name, dto.DeviceType, dto.SerialNumber);
            return Ok(new { success = true, device });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    // POST: /api/sensor/ingest
    [HttpPost("ingest")]
    public async Task<IActionResult> IngestReading([FromBody] IngestReadingDto dto)
    {
        try
        {
            var reading = await _ingestion.IngestReadingAsync(dto.DeviceId, dto.ReadingType, dto.Value, dto.Unit);
            return Ok(new { success = true, reading });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    // GET: /api/sensor/zone/{zoneId}/telemetry
    [HttpGet("zone/{zoneId}/telemetry")]
    public async Task<IActionResult> GetZoneTelemetry(int zoneId)
    {
        try
        {
            var telemetry = await _ingestion.GetLatestTelemetryAsync(zoneId);
            return Ok(telemetry);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // GET: /api/sensor/zone/{zoneId}/status
    [HttpGet("zone/{zoneId}/status")]
    public async Task<IActionResult> GetZoneStatus(int zoneId)
    {
        try
        {
            var status = await _ingestion.GetZoneStatusAsync(zoneId);
            return Ok(status);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // POST: /api/sensor/stale
    [HttpPost("stale")]
    public async Task<IActionResult> MarkStale([FromQuery] int timeoutMinutes = 15)
    {
        await _ingestion.MarkStaleDevicesAsync(timeoutMinutes);
        return Ok(new { success = true, message = "Stale devices marked offline" });
    }
}

public class RegisterDeviceDto
{
    public int ZoneId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public string? SerialNumber { get; set; }
}

public class IngestReadingDto
{
    public int DeviceId { get; set; }
    public string ReadingType { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public string? Unit { get; set; }
}