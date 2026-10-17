using Microsoft.AspNetCore.Mvc;
using AgritechPortal.Models;
using AgritechPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace AgritechPortal.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SensorTelemetryController : ControllerBase
    {
        private readonly AppDbContext _context;

        public SensorTelemetryController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetTelemetry([FromQuery] int? operatorId, [FromQuery] int limit = 20)
        {
            var query = _context.SensorTelemetries.AsQueryable();
            if (operatorId.HasValue)
                query = query.Where(t => t.OperatorId == operatorId.Value);
            var telemetry = await query.OrderByDescending(t => t.RecordedAt).Take(limit).ToListAsync();
            return Ok(telemetry);
        }

        [HttpPost]
        public async Task<IActionResult> AddTelemetry([FromBody] SensorTelemetry telemetry)
        {
            telemetry.RecordedAt = DateTime.UtcNow;
            _context.SensorTelemetries.Add(telemetry);
            await _context.SaveChangesAsync();
            return Ok(telemetry);
        }
    }
}