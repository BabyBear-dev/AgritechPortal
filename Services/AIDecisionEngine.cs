using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AgritechPortal.Data;
using AgritechPortal.Models;

namespace AgritechPortal.Services;

public class AIDecisionEngine
{
    private readonly HttpClient _httpClient;
    private readonly AppDbContext _db;
    private readonly ILogger<AIDecisionEngine> _logger;
    private readonly string _groqApiKey;

    // ✅ Single source of truth for the Groq model
    private const string GroqModel = "openai/gpt-oss-120b";

    public AIDecisionEngine(
        HttpClient httpClient,
        AppDbContext db,
        ILogger<AIDecisionEngine> logger,
        IConfiguration config)
    {
        _httpClient = httpClient;
        _db = db;
        _logger = logger;
        _groqApiKey = config["Groq:ApiKey"] ?? "";
    }

    public async Task<Recommendation?> GenerateRecommendationAsync(int zoneId)
    {
        try
        {
            var zone = await _db.Zones
                .Include(z => z.SensorReadings)
                .FirstOrDefaultAsync(z => z.Id == zoneId);
            if (zone == null)
            {
                _logger.LogWarning("Zone {ZoneId} not found", zoneId);
                return null;
            }

            var latestReadings = zone.SensorReadings
                .GroupBy(r => r.ReadingType)
                .Select(g => g.OrderByDescending(r => r.Timestamp).First())
                .ToList();

            if (!latestReadings.Any())
            {
                _logger.LogWarning("No sensor data for zone {ZoneId}", zoneId);
                return null;
            }

            var readingsText = string.Join("\n", latestReadings.Select(r =>
                $"  - {r.ReadingType}: {r.Value}{r.Unit}"));

            var systemPrompt = "You output ONLY valid JSON. No markdown, no code fences, no explanation.";

            var prompt = $@"Analyze greenhouse sensor data and return JSON.

Crop: {zone.Crop}
Stage: {zone.GrowthStage}
Readings:
{readingsText}

Return exactly this JSON structure:
{{""title"": ""short title"", ""description"": ""actionable advice"", ""confidence"": 75, ""evidence"": [""sensor: value""], ""reasoning"": ""why""}}";

            // Try Groq first
            string raw = "";
            if (!string.IsNullOrEmpty(_groqApiKey))
                raw = await AskGroqAsync(systemPrompt, prompt);

            // Fallback to Ollama
            if (string.IsNullOrEmpty(raw))
            {
                _logger.LogWarning("Groq failed — falling back to Ollama for zone {ZoneId}", zoneId);
                raw = await AskOllamaAsync(prompt);
            }

            if (string.IsNullOrEmpty(raw))
            {
                _logger.LogError("Both Groq and Ollama failed for zone {ZoneId}", zoneId);
                return null;
            }

            // Extract JSON
            int start = raw.IndexOf('{');
            int end = raw.LastIndexOf('}') + 1;
            if (start >= 0 && end > start) raw = raw.Substring(start, end - start);

            using var aiDoc = JsonDocument.Parse(raw);
            var root = aiDoc.RootElement;

            var rec = new Recommendation
            {
                ZoneId = zoneId,
                Title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "Recommendation" : "Recommendation",
                Description = root.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                Confidence = root.TryGetProperty("confidence", out var c) ? c.GetInt32() : 50,
                Status = "pending",
                CreatedAt = DateTime.UtcNow
            };

            var evidence = root.TryGetProperty("evidence", out var ev)
                ? ev.EnumerateArray().Select(e => e.GetString() ?? "").ToList()
                : new List<string>();

            var reasoning = root.TryGetProperty("reasoning", out var re) ? re.GetString() ?? "" : "";

            rec.ActionParams = JsonSerializer.Serialize(new { evidence, reasoning });

            _db.Recommendations.Add(rec);
            await _db.SaveChangesAsync();
            _logger.LogInformation("Generated recommendation for zone {ZoneId}: {Title}", zoneId, rec.Title);
            return rec;
        }
        catch (JsonException jex)
        {
            _logger.LogError(jex, "AI returned invalid JSON for zone {ZoneId}", zoneId);
            return null;
        }
        catch (TaskCanceledException)
        {
            _logger.LogError("AI timed out for zone {ZoneId}", zoneId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate recommendation for zone {ZoneId}", zoneId);
            return null;
        }
    }

    private async Task<string> AskGroqAsync(string system, string user)
    {
        try
        {
            var body = new
            {
                model = GroqModel,
                messages = new[]
                {
                    new { role = "system", content = system },
                    new { role = "user", content = user }
                },
                temperature = 0.1,
                max_tokens = 300,
                response_format = new { type = "json_object" }
            };

            var json = JsonSerializer.Serialize(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var req = new HttpRequestMessage(HttpMethod.Post,
                "https://api.groq.com/openai/v1/chat/completions");
            req.Headers.Add("Authorization", $"Bearer {_groqApiKey}");
            req.Content = content;

            var sw = Stopwatch.StartNew();
            using var resp = await _httpClient.SendAsync(req);
            var respStr = await resp.Content.ReadAsStringAsync();
            sw.Stop();

            _logger.LogInformation("🕐 Groq (decision) responded in {Seconds:F1}s", sw.Elapsed.TotalSeconds);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Groq error {Status}: {Body}", (int)resp.StatusCode, respStr);
                return "";
            }

            using var doc = JsonDocument.Parse(respStr);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Groq exception");
            return "";
        }
    }

    private async Task<string> AskOllamaAsync(string prompt)
    {
        try
        {
            var body = new
            {
                model = "llama3.2:1b",
                messages = new[] { new { role = "user", content = prompt } },
                stream = false,
                options = new { temperature = 0.1, num_predict = 250, num_ctx = 2048 }
            };

            var json = JsonSerializer.Serialize(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            _httpClient.DefaultRequestHeaders.Clear();

            var sw = Stopwatch.StartNew();
            var resp = await _httpClient.PostAsync("http://localhost:11434/api/chat", content);
            var respStr = await resp.Content.ReadAsStringAsync();
            sw.Stop();

            _logger.LogInformation("🕐 Ollama (decision fallback) responded in {Seconds:F1}s", sw.Elapsed.TotalSeconds);

            if (!resp.IsSuccessStatusCode) return "";

            using var doc = JsonDocument.Parse(respStr);
            return doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama exception");
            return "";
        }
    }
}