using Microsoft.AspNetCore.Mvc;
using AgritechPortal.Models;
using AgritechPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace AgritechPortal.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ActuatorsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ActuatorsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetActuators([FromQuery] int? zoneId)
        {
            var query = _context.Actuators.AsQueryable();
            if (zoneId.HasValue)
                query = query.Where(a => a.ZoneId == zoneId.Value);
            var actuators = await query.ToListAsync();
            return Ok(actuators);
        }

        [HttpPut("{id}/status")]
        public async Task<IActionResult> ToggleStatus(int id, [FromBody] bool status)
        {
            var actuator = await _context.Actuators.FindAsync(id);
            if (actuator == null) return NotFound();
            actuator.IsOnline = status;
            await _context.SaveChangesAsync();

            // Log action
            var log = new ActionLog
            {
                ZoneId = actuator.ZoneId,
                Action = "Toggled Actuator",
                Details = $"{actuator.DeviceName} set to {(status ? "ON" : "OFF")}"
            };
            _context.ActionLogs.Add(log);
            await _context.SaveChangesAsync();

            return Ok(actuator);
        }
    }
}