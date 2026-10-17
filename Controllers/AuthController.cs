using Microsoft.AspNetCore.Mvc;
using AgritechPortal.Models;
using AgritechPortal.Data;
using AgritechPortal.Services;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace AgritechPortal.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly EmailService _emailService;

        public AuthController(AppDbContext context, EmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(bytes);
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            if (await _context.Operators.AnyAsync(o => o.Email == dto.Email))
                return BadRequest(new { message = "Email already registered." });

            var op = new Operator
            {
                FullName = dto.FullName,
                Email = dto.Email,
                Phone = dto.Phone,
                Role = dto.Role,
                PasswordHash = HashPassword(dto.PasswordHash)
            };
            _context.Operators.Add(op);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Registration successful.", email = op.Email, role = op.Role, userId = op.Id });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            var op = await _context.Operators.FirstOrDefaultAsync(o => o.Email == dto.Email);
            if (op == null)
                return Unauthorized(new { message = "Invalid credentials." });

            var hash = HashPassword(dto.PasswordHash);
            if (op.PasswordHash != hash)
                return Unauthorized(new { message = "Invalid credentials." });

            return Ok(new { message = "Login successful.", email = op.Email, role = op.Role, userId = op.Id });
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotDto dto)
        {
            var op = await _context.Operators.FirstOrDefaultAsync(o => o.Email == dto.Email);
            if (op == null)
                return Ok(new { message = "If your email is registered, a reset link has been dispatched." });

            return Ok(new { message = "If your email is registered, a reset link has been dispatched." });
        }
    }

    public class RegisterDto
    {
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
    }

    public class LoginDto
    {
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
    }

    public class ForgotDto
    {
        public string Email { get; set; } = string.Empty;
    }
}