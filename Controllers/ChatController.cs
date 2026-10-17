using Microsoft.AspNetCore.Mvc;
using AgritechPortal.Data;
using AgritechPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace AgritechPortal.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<ChatController> _logger;

    public ChatController(AppDbContext db, ILogger<ChatController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ============================================================
    // GET /api/chat/conversations?email=X
    // One entry per conversation with preview + stats
    // ============================================================
    [HttpGet("conversations")]
    public async Task<IActionResult> GetConversations([FromQuery] string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { error = "email is required" });

        try
        {
            var all = await _db.ChatMessages
                .Where(m => m.OperatorEmail == email)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();

            var conversations = all
                .GroupBy(m => m.ConversationId ?? $"legacy-{m.Id}")
                .Select(g =>
                {
                    var ordered = g.OrderBy(m => m.CreatedAt).ToList();
                    var firstUser = ordered.FirstOrDefault(m => m.Sender == "user");
                    var preview = firstUser?.Content ?? ordered.First().Content;
                    if (preview.Length > 60) preview = preview.Substring(0, 60) + "…";

                    return new
                    {
                        conversationId = g.Key,
                        preview,
                        messageCount = ordered.Count,
                        startedAt = ordered.First().CreatedAt,
                        lastMessageAt = ordered.Last().CreatedAt
                    };
                })
                .OrderByDescending(c => c.lastMessageAt)
                .ToList();

            return Ok(conversations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetConversations failed for {Email}", email);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ============================================================
    // GET /api/chat/history?email=X&conversationId=Y
    // ============================================================
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] string email,
        [FromQuery] string? conversationId = null,
        [FromQuery] int limit = 500)
    {
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { error = "email is required" });

        try
        {
            var query = _db.ChatMessages.Where(m => m.OperatorEmail == email);

            if (!string.IsNullOrWhiteSpace(conversationId))
                query = query.Where(m => m.ConversationId == conversationId);

            var total = await query.CountAsync();

            // Load entities first — Sources is JSON and must be deserialized client-side
            var rows = await query
                .OrderBy(m => m.CreatedAt)
                .Take(limit)
                .ToListAsync();

            var messages = rows.Select(m => new
            {
                m.Id,
                m.Sender,
                m.Content,
                m.ZoneId,
                m.ConversationId,
                m.CreatedAt,
                Sources = string.IsNullOrWhiteSpace(m.Sources)
                    ? new List<string>()
                    : SafeDeserializeSources(m.Sources!)
            }).ToList();

            return Ok(new { total, messages });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetHistory failed for {Email}", email);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Defensive JSON deserializer — never throws, returns empty list on bad data.
    /// </summary>
    private static List<string> SafeDeserializeSources(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json)
                   ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    // ============================================================
    // GET /api/chat/history/stats?email=X
    // ============================================================
    [HttpGet("history/stats")]
    public async Task<IActionResult> GetStats([FromQuery] string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { error = "email is required" });

        try
        {
            var msgs = await _db.ChatMessages
                .Where(m => m.OperatorEmail == email)
                .ToListAsync();

            if (msgs.Count == 0)
                return Ok(new
                {
                    totalMessages = 0,
                    userMessages = 0,
                    aiMessages = 0,
                    conversationCount = 0,
                    daysActive = 0
                });

            return Ok(new
            {
                totalMessages = msgs.Count,
                userMessages = msgs.Count(m => m.Sender == "user"),
                aiMessages = msgs.Count(m => m.Sender == "ai"),
                conversationCount = msgs
                    .Select(m => m.ConversationId ?? $"legacy-{m.Id}")
                    .Distinct()
                    .Count(),
                daysActive = msgs.Select(m => m.CreatedAt.Date).Distinct().Count()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetStats failed for {Email}", email);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ============================================================
    // GET /api/chat/history/search?email=X&q=term
    // ============================================================
    [HttpGet("history/search")]
    public async Task<IActionResult> SearchHistory(
        [FromQuery] string email,
        [FromQuery] string q,
        [FromQuery] int limit = 100)
    {
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { error = "email is required" });

        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { error = "q is required" });

        try
        {
            var term = q.Trim().ToLower();

            var matches = await _db.ChatMessages
                .Where(m => m.OperatorEmail == email && m.Content.ToLower().Contains(term))
                .OrderByDescending(m => m.CreatedAt)
                .Take(limit)
                .Select(m => new
                {
                    m.Id,
                    m.Sender,
                    m.Content,
                    m.ConversationId,
                    m.CreatedAt
                })
                .ToListAsync();

            return Ok(new { count = matches.Count, query = q, results = matches });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SearchHistory failed for {Email}", email);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ============================================================
    // POST /api/chat/message
    // ============================================================
    [HttpPost("message")]
    public async Task<IActionResult> SaveMessage([FromBody] SaveChatMessageDto dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.Content))
            return BadRequest(new { error = "Content is required" });

        if (string.IsNullOrWhiteSpace(dto.OperatorEmail))
            return BadRequest(new { error = "OperatorEmail is required" });

        try
        {
            var msg = new ChatMessage
            {
                Sender = (dto.Sender ?? "user").ToLowerInvariant(),
                Content = dto.Content.Trim(),
                OperatorEmail = dto.OperatorEmail,
                ConversationId = dto.ConversationId,
                ZoneId = dto.ZoneId,
                CreatedAt = DateTime.UtcNow
            };

            _db.ChatMessages.Add(msg);
            await _db.SaveChangesAsync();

            return Ok(new { id = msg.Id, conversationId = msg.ConversationId, createdAt = msg.CreatedAt });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SaveMessage failed for {Email}", dto.OperatorEmail);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ============================================================
    // DELETE /api/chat/conversation?email=X&conversationId=Y
    // ============================================================
    [HttpDelete("conversation")]
    public async Task<IActionResult> DeleteConversation(
        [FromQuery] string email,
        [FromQuery] string conversationId)
    {
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { error = "email is required" });

        if (string.IsNullOrWhiteSpace(conversationId))
            return BadRequest(new { error = "conversationId is required" });

        try
        {
            var msgs = await _db.ChatMessages
                .Where(m => m.OperatorEmail == email && m.ConversationId == conversationId)
                .ToListAsync();

            _db.ChatMessages.RemoveRange(msgs);
            await _db.SaveChangesAsync();

            return Ok(new { message = $"Deleted conversation with {msgs.Count} messages" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DeleteConversation failed for {Email}", email);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ============================================================
    // DELETE /api/chat/history?email=X — wipe everything
    // ============================================================
    [HttpDelete("history")]
    public async Task<IActionResult> ClearHistory([FromQuery] string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { error = "email is required" });

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

    // ============================================================
    // DELETE /api/chat/history/{id}?email=X — single message
    // ============================================================
    [HttpDelete("history/{id}")]
    public async Task<IActionResult> DeleteMessage(int id, [FromQuery] string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { error = "email is required" });

        try
        {
            var msg = await _db.ChatMessages.FirstOrDefaultAsync(m => m.Id == id && m.OperatorEmail == email);
            if (msg == null) return NotFound(new { error = "Message not found" });

            _db.ChatMessages.Remove(msg);
            await _db.SaveChangesAsync();
            return Ok(new { message = "Deleted" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    public class SaveChatMessageDto
    {
        public string Sender { get; set; } = "user";
        public string Content { get; set; } = "";
        public string OperatorEmail { get; set; } = "";
        public string? ConversationId { get; set; }
        public int? ZoneId { get; set; }
    }
}