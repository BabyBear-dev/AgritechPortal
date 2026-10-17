using Microsoft.AspNetCore.Mvc;
using AgritechPortal.Models;
using AgritechPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace AgritechPortal.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OperatorsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public OperatorsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetOperators()
        {
            var operators = await _context.Operators.ToListAsync();
            return Ok(operators);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetOperator(int id)
        {
            var op = await _context.Operators.FindAsync(id);
            if (op == null) return NotFound();
            return Ok(op);
        }

        [HttpPost]
        public async Task<IActionResult> CreateOperator([FromBody] Operator op)
        {
            op.CreatedAt = DateTime.UtcNow;
            _context.Operators.Add(op);
            await _context.SaveChangesAsync();
            return Ok(op);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateOperator(int id, [FromBody] Operator updated)
        {
            var op = await _context.Operators.FindAsync(id);
            if (op == null) return NotFound();
            op.FullName = updated.FullName;
            op.Email = updated.Email;
            op.Phone = updated.Phone;
            op.Role = updated.Role;
            await _context.SaveChangesAsync();
            return Ok(op);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteOperator(int id)
        {
            var op = await _context.Operators.FindAsync(id);
            if (op == null) return NotFound();
            _context.Operators.Remove(op);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}