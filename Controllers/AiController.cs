
using Microsoft.AspNetCore.Mvc;
using AgritechPortal.Services;

namespace AgritechPortal.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AiController : ControllerBase
    {
        private readonly AiAssistantService _aiService;
        private readonly FarmMemoryService _memory;

        public AiController(
            AiAssistantService aiService,
            FarmMemoryService memory)
        {
            _aiService = aiService;
            _memory = memory;
        }

        [HttpPost("chat")]
        public async Task<IActionResult> Chat(
            [FromBody] ChatMessageDto dto)
        {
            Console.WriteLine(
                $"🐛 AiController: message='{dto.Message}' | " +
                $"zoneId={dto.ZoneId} | " +
                $"email='{dto.OperatorEmail}' | " +
                $"conv='{dto.ConversationId}'");

            var response = await _aiService.GetChatResponse(
                dto.Message,
                dto.ZoneId,
                dto.OperatorEmail,
                dto.ConversationId
            );

            Console.WriteLine(
                $"📤 Reply via [{string.Join(", ", response.Sources)}]");

            if (dto.ZoneId.HasValue)
            {
                await _memory.SaveAsync(
                    dto.ZoneId.Value,
                    dto.Message,
                    response.Reply);
            }

            return Ok(new
            {
                reply = response.Reply,
                sources = response.Sources
            });
        }
    }

    public class ChatMessageDto
    {
        public string Message { get; set; } = string.Empty;

        public int? ZoneId { get; set; }

        public string? OperatorEmail { get; set; }

        public string? ConversationId { get; set; }
    }
}
