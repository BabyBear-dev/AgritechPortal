using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using AgritechPortal.Data;
using AgritechPortal.Models;

namespace AgritechPortal.Services;

public record ChatResponse(string Reply, List<string> Sources);

public class AiAssistantService
{
    private readonly HttpClient _httpClient;
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly WeatherApiService _weather;
    private readonly DataIngestionService _dataIngestion;
    private readonly PerenualService _perenual;
    private readonly ILogger<AiAssistantService> _logger;

    private readonly string _groqApiKey;
    private readonly string _groqModel;

    private readonly bool _searxEnabled;
    private readonly string _searxBaseUrl;

    private readonly bool _preferOllama;
    private readonly string _ollamaUrl;
    private readonly string _ollamaModel;

    private const string GroqUrl =
        "https://api.groq.com/openai/v1/chat/completions";

    private static readonly string[] KnownPlants =
    {
        "tomato",
        "tomatoes",
        "cucumber",
        "cucumbers",
        "lettuce",
        "spinach",
        "pepper",
        "peppers",
        "chilli",
        "chilli pepper",
        "eggplant",
        "aubergine",
        "carrot",
        "beans",
        "bean",
        "potato",
        "potatoes",
        "onion",
        "onions",
        "cabbage",
        "broccoli",
        "cauliflower",
        "kale",
        "peas",
        "pea",
        "corn",
        "maize",
        "strawberry",
        "strawberries",
        "herbs",
        "basil",
        "mint",
        "parsley",
        "coriander",
        "cilantro",
        "rosemary",
        "thyme",
        "monstera",
        "rose",
        "roses"
    };

    private static readonly HashSet<string> StopWords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "the",
            "a",
            "an",
            "for",
            "to",
            "of",
            "on",
            "in",
            "my",
            "our",
            "me",
            "please",
            "can",
            "you",
            "tell",
            "give",
            "what",
            "how",
            "is",
            "are",
            "do",
            "does",
            "according",
            "based",
            "regarding",
            "from",
            "perenual",
            "plant",
            "plants",
            "crop",
            "crops",
            "requirement",
            "requirements",
            "information",
            "care",
            "needs",
            "need"
        };

    public AiAssistantService(
        HttpClient httpClient,
        AppDbContext db,
        IConfiguration config,
        WeatherApiService weather,
        DataIngestionService dataIngestion,
        PerenualService perenual,
        ILogger<AiAssistantService> logger)
    {
        _httpClient = httpClient;
        _db = db;
        _config = config;
        _weather = weather;
        _dataIngestion = dataIngestion;
        _perenual = perenual;
        _logger = logger;

        _groqApiKey =
            config["Groq:ApiKey"] ??
            Environment.GetEnvironmentVariable("GROQ_API_KEY") ??
            string.Empty;

        _groqModel =
            config["Groq:Model"] ??
            "openai/gpt-oss-120b";

        _searxEnabled =
            config.GetValue<bool>(
                "SearXNG:Enabled",
                true);

        _searxBaseUrl =
            (
                config["SearXNG:BaseUrl"] ??
                "https://searx.be"
            ).TrimEnd('/');

        _preferOllama =
            config.GetValue<bool>(
                "Ollama:Preferred",
                false);

        _ollamaUrl =
            (
                config["Ollama:Url"] ??
                "http://localhost:11434"
            ).TrimEnd('/');

        _ollamaModel =
            config["Ollama:Model"] ??
            "llama3.2:1b";

        _httpClient.Timeout =
            TimeSpan.FromSeconds(45);

        Console.WriteLine(
            $"🤖 AI mode: " +
            $"{(_preferOllama ? "Ollama preferred" : $"Groq ({_groqModel}) → Ollama fallback")} " +
            $"| Web search: {_searxEnabled} ({_searxBaseUrl})");
    }

    // ============================================================
    // MAIN CHAT METHOD
    // ============================================================

    public async Task<ChatResponse> GetChatResponse(
        string message,
        int? zoneId = null,
        string? operatorEmail = null,
        string? conversationId = null)
    {
        var availableSources =
            new List<string>();

        try
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return new ChatResponse(
                    "Please enter a question.",
                    new List<string>());
            }

            message = message.Trim();

            // ========================================================
            // PLANT-CARE / PERENUAL DETECTION
            // ========================================================

            var plantCareQuestion =
                IsPlantCareQuestion(message);

            var requestedField =
                GetRequestedPerenualField(message);

            /*
             * A recognized plant-care field is handled through the
             * Perenual-first flow.
             *
             * Perenual is used first.
             *
             * If Perenual has the requested field:
             *      Perenual information is authoritative.
             *
             * If Perenual does NOT have the requested field:
             *      Groq may fill the missing information using
             *      general agricultural knowledge.
             *
             * Groq must clearly distinguish the fallback information
             * from information actually supplied by Perenual.
             */
            var perenualFocused =
                IsPerenualOnlyQuestion(message) ||
                (plantCareQuestion &&
                 requestedField != null);

            _logger.LogInformation(
                "🌱 Source policy: perenualFocused={PerenualFocused} | plantCare={PlantCare} | requestedField='{RequestedField}'",
                perenualFocused,
                plantCareQuestion,
                requestedField);

            // ========================================================
            // FARM CONTEXT
            // ========================================================

            string farmName =
                "Unknown Farm";

            string farmLocation =
                "Not set";

            string zoneInfo =
                "No zone selected.";

            string sensorData =
                "No sensor data available.";

            string weatherInfo =
                "Current weather data unavailable.";

            string zoneCrop =
                "";

            Farm? farm = null;

            // --------------------------------------------------------
            // FARM
            // --------------------------------------------------------

            try
            {
                farm =
                    await _db.Farms
                        .FirstOrDefaultAsync();

                if (farm != null)
                {
                    farmName =
                        string.IsNullOrWhiteSpace(farm.Name)
                            ? "My Farm"
                            : farm.Name;

                    var locationParts =
                        new List<string>();

                    if (!string.IsNullOrWhiteSpace(
                            farm.Address))
                    {
                        locationParts.Add(
                            farm.Address!);
                    }

                    if (farm.Latitude.HasValue &&
                        farm.Longitude.HasValue)
                    {
                        locationParts.Add(
                            $"({farm.Latitude.Value:F4}, {farm.Longitude.Value:F4})");
                    }

                    if (locationParts.Count > 0)
                    {
                        farmLocation =
                            string.Join(
                                " ",
                                locationParts);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "⚠️ Farm context failed.");
            }

            // --------------------------------------------------------
            // ZONE + TELEMETRY
            // --------------------------------------------------------

            try
            {
                Zone? zone =
                    zoneId.HasValue
                        ? await _db.Zones
                            .Include(z => z.Greenhouse)
                            .FirstOrDefaultAsync(
                                z => z.Id == zoneId.Value)
                        : await _db.Zones
                            .Include(z => z.Greenhouse)
                            .FirstOrDefaultAsync();

                if (zone != null)
                {
                    zoneInfo =
                        $"Zone '{zone.Name}' | " +
                        $"Crop: {zone.Crop} | " +
                        $"Stage: {zone.GrowthStage}";

                    zoneCrop =
                        zone.Crop ?? string.Empty;

                    var telemetry =
                        await _dataIngestion
                            .GetLatestTelemetryAsync(
                                zone.Id);

                    if (telemetry.Any())
                    {
                        sensorData =
                            string.Join(
                                " | ",
                                telemetry.Select(kv =>
                                    $"{kv.Key}: {kv.Value.Value}{kv.Value.Unit}"));

                        availableSources.Add(
                            "sensors");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "⚠️ Zone/telemetry context failed.");
            }

            // ========================================================
            // PERENUAL-FIRST PLANT CARE FLOW
            // ========================================================

            /*
             * This flow happens before weather and web-search context.
             *
             * This means:
             *
             * "What soil requirements does cucumber have according
             *  to Perenual?"
             *
             * will NOT accidentally receive weather/sensor/search
             * information.
             *
             * Perenual is checked first.
             *
             * If Perenual contains soil information:
             *      Groq explains the Perenual information.
             *
             * If Perenual does not contain soil information:
             *      Groq is explicitly allowed to provide general
             *      agricultural knowledge.
             */

            if (perenualFocused)
            {
                var targetPlant =
                    ExtractPlantName(
                        message,
                        zoneCrop);

                _logger.LogInformation(
                    "🌱 Perenual target plant: '{Plant}'",
                    targetPlant);

                if (string.IsNullOrWhiteSpace(
                        targetPlant))
                {
                    return new ChatResponse(
                        "I could not identify the plant in your question.",
                        new List<string>());
                }

                // ----------------------------------------------------
                // IDENTIFY SCIENTIFIC NAME
                // ----------------------------------------------------

                var scientificName =
                    await GetScientificPlantNameAsync(
                        targetPlant);

                if (string.IsNullOrWhiteSpace(
                        scientificName))
                {
                    scientificName =
                        targetPlant;
                }

                _logger.LogInformation(
                    "🧠 Groq scientific name: '{ScientificName}'",
                    scientificName);

                // ----------------------------------------------------
                // PERENUAL LOOKUP
                // ----------------------------------------------------

                PerenualCareResult perenualResult;

                try
                {
                    perenualResult =
                        await _perenual
                            .GetCareResultAsync(
                                scientificName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "🌱 Perenual lookup failed.");

                    return new ChatResponse(
                        "Perenual could not be queried successfully for this plant.",
                        new List<string>
                        {
                            "perenual"
                        });
                }

                // ----------------------------------------------------
                // STATUS MUST BE PRESERVED
                // ----------------------------------------------------

                if (perenualResult.Status !=
                    PerenualLookupStatus.Success)
                {
                    return new ChatResponse(
                        BuildPerenualStatusReply(
                            perenualResult,
                            requestedField),
                        new List<string>
                        {
                            "perenual"
                        });
                }

                availableSources.Add(
                    "perenual");

                // ----------------------------------------------------
                // CHECK WHETHER PERENUAL HAS REQUESTED FIELD
                // ----------------------------------------------------

                var hasPerenualField =
                    HasRequestedPerenualField(
                        perenualResult,
                        requestedField);

                _logger.LogInformation(
                    "🌱 Perenual field '{RequestedField}' available={Available}",
                    requestedField,
                    hasPerenualField);

                /*
                 * IMPORTANT:
                 *
                 * We DO NOT return a missing-field response anymore.
                 *
                 * Instead, Groq receives the Perenual data plus an
                 * explicit instruction:
                 *
                 * - use Perenual when available
                 * - fill missing information with general knowledge
                 * - clearly label the fallback
                 */

                // ----------------------------------------------------
                // PERENUAL-FIRST GROQ PROMPT
                // ----------------------------------------------------

                var strictPrompt =
                    BuildStrictPerenualPrompt(
                        message,
                        perenualResult.PlantName,
                        perenualResult.ScientificName,
                        perenualResult.Context,
                        requestedField,
                        hasPerenualField);

                string rawStrictReply =
                    string.Empty;

                // ----------------------------------------------------
                // GROQ
                // ----------------------------------------------------

                if (!_preferOllama &&
                    !string.IsNullOrWhiteSpace(
                        _groqApiKey))
                {
                    rawStrictReply =
                        await AskGroqAsync(
                            strictPrompt,
                            message);

                    if (!string.IsNullOrWhiteSpace(
                            rawStrictReply))
                    {
                        availableSources.Add(
                            "groq");

                        var (strictReply, _) =
                            ExtractCitations(
                                rawStrictReply);

                        Console.WriteLine(
                            $"📤 Perenual-first response via [groq, perenual] | Perenual field available={hasPerenualField}");

                        return new ChatResponse(
                            strictReply,
                            new List<string>
                            {
                                "groq",
                                "perenual"
                            });
                    }

                    Console.WriteLine(
                        "⚠️ Groq failed — Perenual-first request falling back to Ollama.");
                }

                // ----------------------------------------------------
                // OLLAMA FALLBACK
                // ----------------------------------------------------

                rawStrictReply =
                    await AskOllamaAsync(
                        strictPrompt,
                        message);

                if (!string.IsNullOrWhiteSpace(
                        rawStrictReply))
                {
                    availableSources.Add(
                        "ollama");
                }

                var (ollamaStrictReply, _) =
                    ExtractCitations(
                        rawStrictReply);

                Console.WriteLine(
                    $"📤 Perenual-first response via [ollama, perenual] | Perenual field available={hasPerenualField}");

                return new ChatResponse(
                    ollamaStrictReply,
                    new List<string>
                    {
                        "ollama",
                        "perenual"
                    });
            }

            // ========================================================
            // NORMAL WEATHER
            // ========================================================

            try
            {
                if (farm?.Latitude.HasValue == true &&
                    farm.Longitude.HasValue)
                {
                    var weather =
                        await _weather
                            .GetCurrentWeatherAsync(
                                farm.Latitude.Value,
                                farm.Longitude.Value);

                    if (weather != null)
                    {
                        weatherInfo =
                            $"{weather.Temperature}°C, " +
                            $"{weather.Humidity}% humidity, " +
                            $"{weather.Description}, " +
                            $"wind {weather.WindSpeed} m/s";

                        if (weather.Rain > 0)
                        {
                            weatherInfo +=
                                $", rain {weather.Rain} mm";
                        }

                        availableSources.Add(
                            "weather");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "⚠️ Weather context failed.");
            }

            // ========================================================
            // OPTIONAL WEB SEARCH
            // ========================================================

            string webInfo =
                string.Empty;

            var wantsSearch =
                _searxEnabled &&
                NeedsWebSearch(message);

            Console.WriteLine(
                $"🔎 Web search decision: needsSearch={wantsSearch} | query='{message}'");

            if (wantsSearch)
            {
                try
                {
                    var searchQuery =
                        BuildSearchQuery(
                            message);

                    if (!string.Equals(
                            searchQuery,
                            message,
                            StringComparison.Ordinal))
                    {
                        Console.WriteLine(
                            $"🔎 Keyword query: '{searchQuery}'");
                    }

                    webInfo =
                        await SearXNGSearchAsync(
                            searchQuery);

                    if (!string.IsNullOrWhiteSpace(
                            webInfo))
                    {
                        Console.WriteLine(
                            $"🌐 Injected web results ({webInfo.Length} chars)");

                        availableSources.Add(
                            "searxng");
                    }
                    else
                    {
                        Console.WriteLine(
                            "⚠️ SearXNG returned 0 usable results.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "⚠️ Web search failed.");
                }
            }

            // ========================================================
            // OPTIONAL PERENUAL CONTEXT FOR NORMAL QUESTIONS
            // ========================================================

            string plantInfo =
                string.Empty;

            if (!string.IsNullOrWhiteSpace(
                    zoneCrop))
            {
                try
                {
                    var care =
                        await _perenual
                            .GetCareContextAsync(
                                zoneCrop);

                    if (!string.IsNullOrWhiteSpace(
                            care))
                    {
                        plantInfo =
                            care;

                        Console.WriteLine(
                            $"🌱 Perenual lookup for '{zoneCrop}' → {care.Length} chars");

                        availableSources.Add(
                            "perenual");
                    }
                    else
                    {
                        Console.WriteLine(
                            $"🌱 Perenual: no care data for '{zoneCrop}'");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "⚠️ Perenual normal-context lookup failed.");
                }
            }

            // ========================================================
            // GENERAL PROMPT
            // ========================================================

            var systemMessage =
                BuildGeneralSystemPrompt(
                    farmName,
                    farmLocation,
                    zoneInfo,
                    sensorData,
                    weatherInfo,
                    plantInfo,
                    webInfo);

            // ========================================================
            // GENERAL AI RESPONSE
            // ========================================================

            string rawReply;

            if (!_preferOllama &&
                !string.IsNullOrWhiteSpace(
                    _groqApiKey))
            {
                rawReply =
                    await AskGroqAsync(
                        systemMessage,
                        message);

                if (!string.IsNullOrWhiteSpace(
                        rawReply))
                {
                    availableSources.Add(
                        "groq");

                    goto ProcessGeneralReply;
                }

                Console.WriteLine(
                    "⚠️ Groq failed — falling back to Ollama.");
            }

            rawReply =
                await AskOllamaAsync(
                    systemMessage,
                    message);

            availableSources.Add(
                "ollama");

            // ========================================================
            // PROCESS GENERAL REPLY
            // ========================================================

        ProcessGeneralReply:

            var (reply, cited) =
                ExtractCitations(
                    rawReply);

            var finalSources =
                new List<string>();

            if (availableSources.Contains(
                    "groq"))
            {
                finalSources.Add(
                    "groq");
            }
            else if (availableSources.Contains(
                         "ollama"))
            {
                finalSources.Add(
                    "ollama");
            }

            foreach (var source in
                     new[]
                     {
                         "perenual",
                         "searxng",
                         "weather",
                         "sensors"
                     })
            {
                if (cited.Contains(source) &&
                    availableSources.Contains(source))
                {
                    finalSources.Add(
                        source);
                }
            }

            Console.WriteLine(
                $"📤 Cited: [{string.Join(", ", cited)}] | " +
                $"Available: [{string.Join(", ", availableSources)}] | " +
                $"Final: [{string.Join(", ", finalSources)}]");

            // ========================================================
            // SAVE CHAT HISTORY
            // ========================================================

            if (!string.IsNullOrWhiteSpace(
                    operatorEmail))
            {
                try
                {
                    _db.ChatMessages.Add(
                        new ChatMessage
                        {
                            Sender = "user",
                            Content = message,
                            OperatorEmail = operatorEmail,
                            ConversationId = conversationId,
                            ZoneId = zoneId,
                            CreatedAt =
                                DateTime.UtcNow
                        });

                    _db.ChatMessages.Add(
                        new ChatMessage
                        {
                            Sender = "ai",
                            Content = reply,
                            Sources =
                                JsonSerializer.Serialize(
                                    finalSources),
                            OperatorEmail = operatorEmail,
                            ConversationId = conversationId,
                            ZoneId = zoneId,
                            CreatedAt =
                                DateTime.UtcNow
                                    .AddMilliseconds(1)
                        });

                    await _db.SaveChangesAsync();

                    Console.WriteLine(
                        $"💾 Chat saved for {operatorEmail} " +
                        $"(conversation: {conversationId ?? "none"})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"⚠️ Failed to save chat history: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine(
                    "⚠️ No operatorEmail provided — chat NOT saved");
            }

            return new ChatResponse(
                reply,
                finalSources);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "❌ AiAssistantService failed.");

            return new ChatResponse(
                $"🌿 AgriAI error: {ex.Message}",
                new List<string>());
        }
    }

    // ============================================================
    // PERENUAL-FIRST PROMPT
    // ============================================================

    private static string BuildStrictPerenualPrompt(
        string message,
        string plantName,
        string scientificName,
        string perenualContext,
        string? requestedField,
        bool hasPerenualField)
    {
        var fieldStatus =
            hasPerenualField
                ? $"Perenual DOES provide information for the requested field: {requestedField ?? "general plant care"}."
                : $"Perenual DOES NOT provide the requested field: {requestedField ?? "general plant care"}.";

        return $"""
You are AgriAI, an agricultural assistant.

USER QUESTION:
"{message}"

PLANT:
Common name: {plantName}
Scientific name: {scientificName}

REQUESTED FIELD:
{requestedField ?? "general plant care"}

PERENUAL FIELD STATUS:
{fieldStatus}

PERENUAL DATA:
--- BEGIN PERENUAL DATA ---
{perenualContext}
--- END PERENUAL DATA ---

SOURCE POLICY:

1. Perenual is the PRIMARY source for plant-care information.

2. When Perenual explicitly provides the information requested by the user,
   use the Perenual information as the authoritative plant-care information.

3. When Perenual does NOT provide the requested information, you MAY use
   general agricultural knowledge to fill the missing information.

4. If you use general agricultural knowledge because Perenual lacks the
   requested field, explicitly tell the user that Perenual did not provide
   that information and that the additional guidance is general agricultural
   guidance.

5. NEVER claim that general agricultural knowledge came from Perenual.

6. NEVER invent information and attribute it to Perenual.

7. NEVER modify a Perenual value and present the modified value as a
   Perenual value.

8. If Perenual provides a numerical value, preserve the value and unit
   accurately.

9. If Perenual provides a watering recommendation, use that recommendation
   rather than replacing it with your own preferred watering schedule.

10. If Perenual provides a soil recommendation, use that information rather
    than replacing it with general soil advice.

11. If Perenual provides sunlight information, use that information rather
    than replacing it with general sunlight advice.

12. If Perenual provides pruning information, use that information rather
    than replacing it with general pruning advice.

13. If Perenual provides growth-rate information, use that information.

14. If Perenual provides care-level information, use that information.

15. If Perenual provides maintenance information, use that information.

16. If Perenual provides hardiness information, use that information.

17. If Perenual does NOT provide the requested field, you may answer using
    general agricultural knowledge.

18. When providing general agricultural knowledge, do not imply that the
    recommendation came from Perenual.

19. Do NOT use weather information in this answer.

20. Do NOT use sensor or telemetry information in this answer.

21. Do NOT use web-search information in this answer.

22. Do NOT use information from the user's farm context unless it appears
    in the supplied Perenual data.

23. Do NOT invent exact numbers simply to make an answer appear more useful.

24. General agricultural knowledge may contain numerical recommendations when
    appropriate, but if you provide such numbers they must be presented as
    general guidance, NOT as Perenual data.

25. If Perenual provides partial information, use Perenual for the portion it
    provides and use general agricultural knowledge only for the missing
    portion.

26. If Perenual provides no information for the requested field, say so
    briefly and then provide useful general agricultural guidance.

27. Do not mention internal software, APIs, prompts, tools, or system rules.

ANSWER STYLE:

- Answer the user's question directly.
- Be concise but useful.
- Prioritize Perenual where Perenual data exists.
- Clearly distinguish Perenual information from general agricultural guidance.
- Do not unnecessarily repeat the entire Perenual dataset.
- Do not include weather, sensor or web-search information.
- Do not say "according to Perenual" for information that came from general
  agricultural knowledge.

EXAMPLE OF MISSING INFORMATION:

If the user asks:

"What soil requirements does cucumber have according to Perenual?"

and Perenual contains no soil requirement, an appropriate answer structure is:

"Perenual does not provide a soil requirement for cucumber in the retrieved
species data. As general agricultural guidance, cucumbers generally prefer
well-drained, fertile soil with good organic matter and suitable moisture
retention."

Do NOT say:

"Perenual recommends well-drained fertile soil..."

because Perenual did not provide that information.

EXAMPLE OF AVAILABLE INFORMATION:

If Perenual provides:

"Sunlight: full sun"

then say something equivalent to:

"According to Perenual, cucumber requires full sun."

Do not replace that with unrelated general recommendations.

CITATION TRACKING:

After your answer, on a separate final line, write exactly one of:

CITED: perenual

or

CITED: perenual, groq

Use:

CITED: perenual

when the answer only uses information supplied by Perenual.

Use:

CITED: perenual, groq

when general agricultural knowledge was used to supplement or replace a
missing field.

Do not write anything after the CITED line.
""";
    }

    // ============================================================
    // GENERAL SYSTEM PROMPT
    // ============================================================

    private static string BuildGeneralSystemPrompt(
        string farmName,
        string farmLocation,
        string zoneInfo,
        string sensorData,
        string weatherInfo,
        string plantInfo,
        string webInfo)
    {
        return $"""
You are AgriAI, a friendly and knowledgeable assistant for greenhouse farmers.

FARM CONTEXT:

Farm:
{farmName}

Location:
{farmLocation}

Zone:
{zoneInfo}

Sensors:
{sensorData}

Current Weather:
{weatherInfo}

{(string.IsNullOrWhiteSpace(plantInfo)
    ? ""
    : $"""
PLANT CARE REFERENCE — PERENUAL:

{plantInfo}

END PLANT CARE REFERENCE
""")}

{(string.IsNullOrWhiteSpace(webInfo)
    ? ""
    : $"""
LIVE WEB RESULTS:

{webInfo}

END LIVE WEB RESULTS
""")}

RULES:

1. Use supplied farm information when answering questions about the user's farm.

2. Never invent sensor readings.

3. Never invent weather readings.

4. Weather information may only come from the CURRENT WEATHER section.

5. Never invent Perenual information.

6. If Perenual provides a plant-care field, prefer that information.

7. Do not claim Perenual provided information that is not present in the
   Perenual data.

8. For normal agricultural questions where Perenual was not explicitly
   required, general agricultural knowledge may be used.

9. Clearly distinguish supplied data from general recommendations.

10. Do not invent exact plant-care quantities, percentages, measurements,
    thresholds, frequencies, or numerical ranges and attribute them to
    Perenual.

11. If a Perenual value is present, preserve its meaning and units.

12. If LIVE WEB RESULTS are present and relevant to the user's question,
    use them.

13. Do not claim a source was used unless the answer actually relied on
    that source.

14. Never state that web results are unavailable when LIVE WEB RESULTS
    are supplied.

15. Answer directly and naturally.

16. Keep answers concise unless the user requests a detailed explanation.

INTERNAL CITATION TRACKING:

After your answer, on a NEW line by itself, write exactly:

CITED: <comma-separated list of sources actually used>

Allowed source tokens:

weather
sensors
perenual
searxng

If no supplied source was used:

CITED: none

Do not add any other text after the CITED line.
""";
    }

    // ============================================================
    // PERENUAL STATUS REPLY
    // ============================================================

    private static string BuildPerenualStatusReply(
        PerenualCareResult result,
        string? requestedField)
    {
        return result.Status switch
        {
            PerenualLookupStatus.RateLimited =>
                $"Perenual is currently rate-limited, so I could not retrieve the requested {requestedField ?? "plant-care"} information for {result.PlantName}.",

            PerenualLookupStatus.NoSpecies =>
                $"Perenual did not return a reliable species match for {result.PlantName}.",

            PerenualLookupStatus.NotFound =>
                $"Perenual could not find the species details for {result.PlantName}.",

            PerenualLookupStatus.Failed =>
                $"Perenual could not retrieve the requested information for {result.PlantName}.",

            _ =>
                $"Perenual could not provide the requested information for {result.PlantName}."
        };
    }

    // ============================================================
    // REQUESTED PERENUAL FIELD
    // ============================================================

    private static string? GetRequestedPerenualField(
        string message)
    {
        var q =
            message.ToLowerInvariant();

        if (q.Contains("water") ||
            q.Contains("watering") ||
            q.Contains("irrigat"))
        {
            return "watering";
        }

        if (q.Contains("sunlight") ||
            q.Contains("sun light") ||
            q.Contains("light requirement") ||
            q.Contains("light needs"))
        {
            return "sunlight";
        }

        if (q.Contains("soil"))
        {
            return "soil";
        }

        if (q.Contains("prun"))
        {
            return "pruning";
        }

        if (q.Contains("growth rate") ||
            q.Contains("grow rate"))
        {
            return "growth rate";
        }

        if (q.Contains("care level") ||
            q.Contains("level of care"))
        {
            return "care level";
        }

        if (q.Contains("maintenance"))
        {
            return "maintenance";
        }

        if (q.Contains("hardiness") ||
            q.Contains("hardiness zone"))
        {
            return "hardiness";
        }

        if (q.Contains("fertilizer") ||
            q.Contains("fertiliser") ||
            q.Contains("fertilization") ||
            q.Contains("fertilisation"))
        {
            return "fertilizer";
        }

        if (q.Contains("pest") ||
            q.Contains("disease") ||
            q.Contains("diseases"))
        {
            return "pests/diseases";
        }

        return null;
    }

    // ============================================================
    // CHECK REQUESTED PERENUAL FIELD
    // ============================================================

    private static bool HasRequestedPerenualField(
        PerenualCareResult result,
        string? requestedField)
    {
        if (string.IsNullOrWhiteSpace(
                requestedField))
        {
            return result.HasAnyCareData;
        }

        return requestedField
            .ToLowerInvariant() switch
        {
            "watering" =>
                !string.IsNullOrWhiteSpace(
                    result.Watering),

            "sunlight" =>
                !string.IsNullOrWhiteSpace(
                    result.Sunlight),

            "soil" =>
                !string.IsNullOrWhiteSpace(
                    result.Soil),

            "pruning" =>
                !string.IsNullOrWhiteSpace(
                    result.Pruning),

            "growth rate" =>
                !string.IsNullOrWhiteSpace(
                    result.GrowthRate),

            "care level" =>
                !string.IsNullOrWhiteSpace(
                    result.CareLevel),

            "maintenance" =>
                !string.IsNullOrWhiteSpace(
                    result.Maintenance),

            "hardiness" =>
                !string.IsNullOrWhiteSpace(
                    result.Hardiness),

            "fertilizer" =>
                false,

            "pests/diseases" =>
                false,

            _ =>
                result.HasAnyCareData
        };
    }

    // ============================================================
    // PERENUAL QUESTION
    // ============================================================

    private static bool IsPerenualOnlyQuestion(
        string message)
    {
        var q =
            message.ToLowerInvariant();

        return
            q.Contains("perenual") ||
            q.Contains("according to perenual") ||
            q.Contains("based on perenual") ||
            q.Contains("from perenual") ||
            q.Contains("perenual data");
    }

    // ============================================================
    // PLANT CARE QUESTION
    // ============================================================

    private static bool IsPlantCareQuestion(
        string message)
    {
        var q =
            message.ToLowerInvariant();

        var hasPlant =
            KnownPlants.Any(
                plant => q.Contains(plant));

        var careTerms =
            new[]
            {
                "water",
                "watering",
                "irrigat",
                "sunlight",
                "sun light",
                "soil",
                "prun",
                "fertiliz",
                "fertilis",
                "maintenance",
                "hardiness",
                "growth rate",
                "care requirement",
                "care needs",
                "plant care",
                "growing requirement",
                "growing needs",
                "pest",
                "disease"
            };

        var hasCareTerm =
            careTerms.Any(
                term => q.Contains(term));

        return
            (hasPlant &&
             hasCareTerm) ||
            IsPerenualOnlyQuestion(message);
    }

    // ============================================================
    // EXTRACT PLANT NAME
    // ============================================================

    private static string ExtractPlantName(
        string message,
        string zoneCrop)
    {
        var lower =
            message.ToLowerInvariant();

        // --------------------------------------------------------
        // 1. Known plant names
        // --------------------------------------------------------

        foreach (var plant in
                 KnownPlants
                     .OrderByDescending(
                         x => x.Length))
        {
            if (lower.Contains(plant))
            {
                return plant;
            }
        }

        // --------------------------------------------------------
        // 2. Scientific name
        // --------------------------------------------------------

        var scientificMatch =
            Regex.Match(
                message,
                @"\b[A-Z][a-z]{2,}\s+[a-z][a-z-]{2,}\b");

        if (scientificMatch.Success)
        {
            return scientificMatch.Value;
        }

        // --------------------------------------------------------
        // 3. Zone crop
        // --------------------------------------------------------

        if (!string.IsNullOrWhiteSpace(
                zoneCrop))
        {
            return zoneCrop.Trim();
        }

        // --------------------------------------------------------
        // 4. "for X according to..."
        // --------------------------------------------------------

        var forMatch =
            Regex.Match(
                message,
                @"\bfor\s+(.+?)(?:\s+according\s+to|\s+based\s+on|\s+regarding|\?|$)",
                RegexOptions.IgnoreCase);

        if (forMatch.Success)
        {
            var candidate =
                CleanExtractedPlantName(
                    forMatch.Groups[1].Value);

            if (!string.IsNullOrWhiteSpace(
                    candidate))
            {
                return candidate;
            }
        }

        // --------------------------------------------------------
        // 5. "tell me about X"
        // --------------------------------------------------------

        var tellMatch =
            Regex.Match(
                message,
                @"\btell\s+me\s+about\s+(.+?)(?:\?|$)",
                RegexOptions.IgnoreCase);

        if (tellMatch.Success)
        {
            var candidate =
                CleanExtractedPlantName(
                    tellMatch.Groups[1].Value);

            if (!string.IsNullOrWhiteSpace(
                    candidate))
            {
                return candidate;
            }
        }

        // --------------------------------------------------------
        // 6. "care requirements for X"
        // --------------------------------------------------------

        var careMatch =
            Regex.Match(
                message,
                @"(?:care|information|requirements|needs|growing|growing requirements|watering|sunlight)\s+(?:requirements\s+)?for\s+(.+?)(?:\?|$)",
                RegexOptions.IgnoreCase);

        if (careMatch.Success)
        {
            var candidate =
                CleanExtractedPlantName(
                    careMatch.Groups[1].Value);

            if (!string.IsNullOrWhiteSpace(
                    candidate))
            {
                return candidate;
            }
        }

        // --------------------------------------------------------
        // 7. "plant X"
        // --------------------------------------------------------

        var plantMatch =
            Regex.Match(
                message,
                @"\bplant\s+(.+?)(?:\?|$)",
                RegexOptions.IgnoreCase);

        if (plantMatch.Success)
        {
            var candidate =
                CleanExtractedPlantName(
                    plantMatch.Groups[1].Value);

            if (!string.IsNullOrWhiteSpace(
                    candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    // ============================================================
    // CLEAN EXTRACTED PLANT
    // ============================================================

    private static string CleanExtractedPlantName(
        string value)
    {
        var result =
            value.Trim();

        result =
            Regex.Replace(
                result,
                @"\s+(?:according|based|regarding)\s+to.*$",
                "",
                RegexOptions.IgnoreCase);

        result =
            Regex.Replace(
                result,
                @"\s+from\s+perenual.*$",
                "",
                RegexOptions.IgnoreCase);

        result =
            Regex.Replace(
                result,
                @"\s+plants?$",
                "",
                RegexOptions.IgnoreCase);

        result =
            result.Trim(
                ' ',
                '?',
                '.',
                ',',
                ':',
                ';',
                '"',
                '\'',
                '`');

        return result;
    }

    // ============================================================
    // SCIENTIFIC NAME IDENTIFICATION
    // ============================================================

    private async Task<string?>
        GetScientificPlantNameAsync(
            string plantName)
    {
        if (string.IsNullOrWhiteSpace(
                plantName))
        {
            return null;
        }

        if (LooksLikeScientificName(
                plantName))
        {
            return plantName.Trim();
        }

        if (string.IsNullOrWhiteSpace(
                _groqApiKey))
        {
            return null;
        }

        var prompt = """
You identify plant scientific names.

Return ONLY valid JSON in exactly this format:

{"scientific_name":"Genus species"}

Rules:
- Identify the most likely scientific name for the supplied plant.
- Use the accepted botanical scientific name.
- Do not provide care information.
- Do not provide explanations.
- Do not invent a hybrid or cultivar unless explicitly supplied.
""";

        var userPrompt =
            $"Plant: {plantName}";

        try
        {
            var raw =
                await AskGroqAsync(
                    prompt,
                    userPrompt);

            if (string.IsNullOrWhiteSpace(
                    raw))
            {
                return null;
            }

            raw =
                raw.Trim();

            raw =
                Regex.Replace(
                    raw,
                    @"^```(?:json)?\s*",
                    "",
                    RegexOptions.IgnoreCase);

            raw =
                Regex.Replace(
                    raw,
                    @"\s*```$",
                    "",
                    RegexOptions.IgnoreCase);

            using var doc =
                JsonDocument.Parse(raw);

            if (doc.RootElement.TryGetProperty(
                    "scientific_name",
                    out var scientificName))
            {
                var result =
                    scientificName.GetString();

                if (!string.IsNullOrWhiteSpace(
                        result) &&
                    LooksLikeScientificName(
                        result))
                {
                    return result.Trim();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "🧠 Could not determine scientific name for '{PlantName}'",
                plantName);
        }

        return null;
    }

    // ============================================================
    // SCIENTIFIC NAME DETECTION
    // ============================================================

    private static bool LooksLikeScientificName(
        string value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return false;
        }

        return Regex.IsMatch(
            value.Trim(),
            @"^[A-Z][a-z]{2,}\s+[a-z][a-z-]{2,}$");
    }

    // ============================================================
    // WEB SEARCH DECISION
    // ============================================================

    private static bool NeedsWebSearch(
        string query)
    {
        var q =
            query.ToLowerInvariant();

        string[] triggers =
        {
            "latest",
            "current",
            "today",
            "tonight",
            "tomorrow",
            "yesterday",
            "news",
            "price",
            "cost",
            "market",
            "forecast",
            "who is",
            "who was",
            "when is",
            "when was",
            "where is",
            "how much",
            "how many",
            "release",
            "2025",
            "2026",
            "2027",
            "weather in",
            "temperature in",
            "exchange rate",
            "what happened",
            "update",
            "recent",
            "newest",
            "search",
            "google",
            "find",
            "look up",
            "tell me about"
        };

        return triggers.Any(
            t => q.Contains(t));
    }

    // ============================================================
    // SEARCH QUERY
    // ============================================================

    private static string BuildSearchQuery(
        string userQuery)
    {
        var q =
            userQuery
                .Replace(
                    "what's",
                    "",
                    StringComparison.OrdinalIgnoreCase)
                .Replace(
                    "what is",
                    "",
                    StringComparison.OrdinalIgnoreCase)
                .Replace(
                    "can you",
                    "",
                    StringComparison.OrdinalIgnoreCase)
                .Replace(
                    "please",
                    "",
                    StringComparison.OrdinalIgnoreCase)
                .Replace(
                    "?",
                    "")
                .Trim();

        var lower =
            q.ToLowerInvariant();

        if (lower.Contains("price") ||
            lower.Contains("cost") ||
            lower.Contains("market"))
        {
            q += " 2026";
        }

        return q.Trim();
    }

    // ============================================================
    // GROQ
    // ============================================================

    private async Task<string>
        AskGroqAsync(
            string system,
            string user)
    {
        try
        {
            var body = new
            {
                model = _groqModel,
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = system
                    },
                    new
                    {
                        role = "user",
                        content = user
                    }
                },
                temperature = 0.3,
                max_tokens = 700
            };

            var json =
                JsonSerializer.Serialize(body);

            using var request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    GroqUrl);

            request.Headers.Add(
                "Authorization",
                $"Bearer {_groqApiKey}");

            request.Content =
                new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json");

            var stopwatch =
                Stopwatch.StartNew();

            using var response =
                await _httpClient.SendAsync(
                    request);

            var responseText =
                await response.Content
                    .ReadAsStringAsync();

            stopwatch.Stop();

            Console.WriteLine(
                $"🕐 Groq responded in " +
                $"{stopwatch.Elapsed.TotalSeconds:F1}s");

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine(
                    $"⚠️ Groq error {(int)response.StatusCode}: " +
                    $"{responseText}");

                return string.Empty;
            }

            using var document =
                JsonDocument.Parse(
                    responseText);

            var content =
                document.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

            return content?.Trim() ??
                   string.Empty;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"⚠️ Groq exception: {ex.Message}");

            return string.Empty;
        }
    }

    // ============================================================
    // OLLAMA
    // ============================================================

    private async Task<string>
        AskOllamaAsync(
            string system,
            string user)
    {
        try
        {
            var body = new
            {
                model = _ollamaModel,
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = system
                    },
                    new
                    {
                        role = "user",
                        content = user
                    }
                },
                stream = false,
                options = new
                {
                    temperature = 0.3,
                    num_predict = 500,
                    num_ctx = 4096
                }
            };

            var json =
                JsonSerializer.Serialize(body);

            using var content =
                new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json");

            var stopwatch =
                Stopwatch.StartNew();

            using var response =
                await _httpClient.PostAsync(
                    $"{_ollamaUrl}/api/chat",
                    content);

            var responseText =
                await response.Content
                    .ReadAsStringAsync();

            stopwatch.Stop();

            Console.WriteLine(
                $"🕐 Ollama responded in " +
                $"{stopwatch.Elapsed.TotalSeconds:F1}s");

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine(
                    $"⚠️ Ollama error {(int)response.StatusCode}: " +
                    responseText);

                return string.Empty;
            }

            using var document =
                JsonDocument.Parse(
                    responseText);

            return document.RootElement
                .GetProperty("message")
                .GetProperty("content")
                .GetString()
                ?.Trim() ??
                string.Empty;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"⚠️ Ollama exception: {ex.Message}");

            return string.Empty;
        }
    }

    // ============================================================
    // SEARXNG
    // ============================================================

    private async Task<string>
        SearXNGSearchAsync(
            string query)
    {
        try
        {
            var url =
                $"{_searxBaseUrl}/search" +
                $"?q={Uri.EscapeDataString(query)}" +
                $"&format=json" +
                $"&language=en";

            using var request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    url);

            request.Headers.Add(
                "User-Agent",
                "Mozilla/5.0 (compatible; AgriAI/1.0)");

            request.Headers.Add(
                "Accept",
                "application/json");

            var stopwatch =
                Stopwatch.StartNew();

            using var response =
                await _httpClient.SendAsync(
                    request);

            var responseText =
                await response.Content
                    .ReadAsStringAsync();

            stopwatch.Stop();

            Console.WriteLine(
                $"🌐 SearXNG responded in " +
                $"{stopwatch.Elapsed.TotalSeconds:F1}s " +
                $"(HTTP {(int)response.StatusCode})");

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine(
                    $"⚠️ SearXNG error {(int)response.StatusCode}");

                return string.Empty;
            }

            var trimmed =
                responseText.TrimStart();

            if (!trimmed.StartsWith(
                    "{",
                    StringComparison.Ordinal))
            {
                Console.WriteLine(
                    "⚠️ SearXNG returned non-JSON.");

                return string.Empty;
            }

            using var document =
                JsonDocument.Parse(
                    responseText);

            var root =
                document.RootElement;

            var output =
                new StringBuilder();

            var found =
                0;

            if (root.TryGetProperty(
                    "results",
                    out var results) &&
                results.ValueKind ==
                    JsonValueKind.Array)
            {
                foreach (var result
                         in results.EnumerateArray())
                {
                    if (found >= 5)
                    {
                        break;
                    }

                    var title =
                        result.TryGetProperty(
                            "title",
                            out var titleElement)
                            ? titleElement.GetString()
                            : string.Empty;

                    var url2 =
                        result.TryGetProperty(
                            "url",
                            out var urlElement)
                            ? urlElement.GetString()
                            : string.Empty;

                    var snippet =
                        result.TryGetProperty(
                            "content",
                            out var contentElement)
                            ? contentElement.GetString()
                            : string.Empty;

                    if (string.IsNullOrWhiteSpace(
                            title))
                    {
                        continue;
                    }

                    output.AppendLine(
                        $"• {title}");

                    if (!string.IsNullOrWhiteSpace(
                            snippet))
                    {
                        output.AppendLine(
                            $"  {Truncate(snippet, 400)}");
                    }

                    if (!string.IsNullOrWhiteSpace(
                            url2))
                    {
                        output.AppendLine(
                            $"  {url2}");
                    }

                    output.AppendLine();

                    found++;
                }
            }

            Console.WriteLine(
                $"📊 SearXNG returned {found} result(s)");

            var finalResult =
                output.ToString().Trim();

            return string.IsNullOrWhiteSpace(
                    finalResult)
                ? string.Empty
                : Truncate(
                    finalResult,
                    3000);
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine(
                "⚠️ SearXNG timeout.");

            return string.Empty;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"⚠️ SearXNG exception: {ex.Message}");

            return string.Empty;
        }
    }

    // ============================================================
    // CITATION EXTRACTION
    // ============================================================

    private static (
        string reply,
        HashSet<string> cited)
        ExtractCitations(
            string raw)
    {
        var cited =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(
                raw))
        {
            return (
                raw ?? string.Empty,
                cited);
        }

        var kept =
            new List<string>();

        foreach (var line
                 in raw.Split('\n'))
        {
            var probe =
                line
                    .Trim()
                    .Trim(
                        '*',
                        '_',
                        '`')
                    .TrimStart(
                        '*',
                        '_',
                        '`')
                    .Trim();

            var match =
                Regex.Match(
                    probe,
                    @"^CITED\s*[:\-]\s*(.+)$",
                    RegexOptions.IgnoreCase);

            if (match.Success)
            {
                var payload =
                    match.Groups[1]
                        .Value
                        .Trim()
                        .Trim(
                            '*',
                            '_',
                            '`');

                foreach (var token
                         in payload.Split(
                             new[]
                             {
                                 ',',
                                 ';',
                                 ' ',
                                 '\t'
                             },
                             StringSplitOptions
                                 .RemoveEmptyEntries))
                {
                    var tokenValue =
                        token
                            .Trim()
                            .ToLowerInvariant()
                            .Trim(
                                '*',
                                '_',
                                '`',
                                '.');

                    if (tokenValue is
                        "none" or
                        "n/a" or
                        "no" or
                        "-" or
                        "nothing" or
                        "general")
                    {
                        continue;
                    }

                    if (tokenValue == "web")
                    {
                        tokenValue =
                            "searxng";
                    }

                    if (tokenValue == "plant" ||
                        tokenValue == "plantcare" ||
                        tokenValue == "plant-care")
                    {
                        tokenValue =
                            "perenual";
                    }

                    if (tokenValue is
                        "weather" or
                        "sensors" or
                        "perenual" or
                        "searxng")
                    {
                        cited.Add(
                            tokenValue);
                    }
                }

                continue;
            }

            kept.Add(line);
        }

        var cleaned =
            string.Join(
                '\n',
                kept).Trim();

        return (
            cleaned,
            cited);
    }

    // ============================================================
    // TRUNCATE
    // ============================================================

    private static string Truncate(
        string value,
        int maxLength)
    {
        if (string.IsNullOrEmpty(value) ||
            value.Length <= maxLength)
        {
            return value;
        }

        return value.Substring(
                   0,
                   maxLength) +
               "…";
    }
}