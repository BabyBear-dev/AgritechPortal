using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgritechPortal.Services;

public enum PerenualLookupStatus
{
    Success,
    RateLimited,
    NoSpecies,
    NotFound,
    Failed
}

public class PerenualCareResult
{
    public PerenualLookupStatus Status { get; set; }

    public string PlantName { get; set; } = string.Empty;

    public string ScientificName { get; set; } = string.Empty;

    public string Watering { get; set; } = string.Empty;

    public string Sunlight { get; set; } = string.Empty;

    public string Soil { get; set; } = string.Empty;

    public string Pruning { get; set; } = string.Empty;

    public string GrowthRate { get; set; } = string.Empty;

    public string CareLevel { get; set; } = string.Empty;

    public string Maintenance { get; set; } = string.Empty;

    public string Hardiness { get; set; } = string.Empty;

    public string Context { get; set; } = string.Empty;

    public bool HasAnyCareData =>
        !string.IsNullOrWhiteSpace(Watering) ||
        !string.IsNullOrWhiteSpace(Sunlight) ||
        !string.IsNullOrWhiteSpace(Soil) ||
        !string.IsNullOrWhiteSpace(Pruning) ||
        !string.IsNullOrWhiteSpace(GrowthRate) ||
        !string.IsNullOrWhiteSpace(CareLevel) ||
        !string.IsNullOrWhiteSpace(Maintenance) ||
        !string.IsNullOrWhiteSpace(Hardiness);
}

public class PerenualService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<PerenualService> _logger;

    private const string BaseUrl =
        "https://perenual.com/api";

    public PerenualService(
        HttpClient httpClient,
        IConfiguration config,
        ILogger<PerenualService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;

        _httpClient.Timeout =
            TimeSpan.FromSeconds(30);
    }

    // ============================================================
    // PUBLIC CARE LOOKUP
    // ============================================================

    public async Task<PerenualCareResult>
        GetCareResultAsync(string plantName)
    {
        plantName = CleanPlantName(plantName);

        if (string.IsNullOrWhiteSpace(plantName))
        {
            return new PerenualCareResult
            {
                Status = PerenualLookupStatus.NoSpecies,
                PlantName = plantName
            };
        }

        _logger.LogInformation(
            "🌱 Querying Perenual with '{PlantName}'",
            plantName);

        var species =
            await FindBestSpeciesAsync(plantName);

        if (species.Status !=
            PerenualLookupStatus.Success)
        {
            return new PerenualCareResult
            {
                Status = species.Status,
                PlantName = plantName,
                ScientificName =
                    species.ScientificName ?? string.Empty
            };
        }

        var details =
            await GetSpeciesDetailsAsync(
                species.SpeciesId);

        if (details.Status !=
            PerenualLookupStatus.Success)
        {
            return new PerenualCareResult
            {
                Status = details.Status,
                PlantName =
                    species.CommonName ?? plantName,
                ScientificName =
                    species.ScientificName ?? string.Empty
            };
        }

        return details.Result!;
    }

    // ============================================================
    // PUBLIC CARE CONTEXT
    // ============================================================

    public async Task<string>
        GetCareContextAsync(string plantName)
    {
        var result =
            await GetCareResultAsync(plantName);

        if (result.Status !=
            PerenualLookupStatus.Success)
        {
            _logger.LogInformation(
                "🌱 Perenual: no care data for '{PlantName}'",
                plantName);

            return string.Empty;
        }

        if (!result.HasAnyCareData)
        {
            _logger.LogInformation(
                "🌱 Perenual: no recognized care data for '{PlantName}'",
                plantName);

            return string.Empty;
        }

        return result.Context;
    }

    // ============================================================
    // SPECIES MATCH RESULT
    // ============================================================

    private sealed class SpeciesMatchResult
    {
        public PerenualLookupStatus Status { get; set; }

        public int SpeciesId { get; set; }

        public string? CommonName { get; set; }

        public string? ScientificName { get; set; }
    }

    // ============================================================
    // DETAILS RESULT
    // ============================================================

    private sealed class DetailsResult
    {
        public PerenualLookupStatus Status { get; set; }

        public PerenualCareResult? Result { get; set; }
    }

    // ============================================================
    // FIND BEST SPECIES
    // ============================================================

    private async Task<SpeciesMatchResult>
        FindBestSpeciesAsync(string plantName)
    {
        try
        {
            _logger.LogInformation(
                "🌱 Perenual species-list request for '{PlantName}'",
                plantName);

            var apiKey = GetApiKey();

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogError(
                    "🌱 Perenual API key is missing.");

                return new SpeciesMatchResult
                {
                    Status =
                        PerenualLookupStatus.Failed
                };
            }

            var url =
                $"{BaseUrl}/species-list" +
                $"?key={Uri.EscapeDataString(apiKey)}" +
                $"&q={Uri.EscapeDataString(plantName)}";

            using var response =
                await _httpClient.GetAsync(url);

            _logger.LogInformation(
                "🌱 Perenual species-list HTTP {StatusCode}",
                (int)response.StatusCode);

            // FIX: HttpStatusCode comparison
            if (response.StatusCode ==
                HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning(
                    "🌱 Perenual species-list returned HTTP 429.");

                return new SpeciesMatchResult
                {
                    Status =
                        PerenualLookupStatus.RateLimited
                };
            }

            // FIX: HttpStatusCode comparison
            if (response.StatusCode ==
                HttpStatusCode.NotFound)
            {
                return new SpeciesMatchResult
                {
                    Status =
                        PerenualLookupStatus.NoSpecies
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "🌱 Perenual species-list failed with HTTP {StatusCode}",
                    (int)response.StatusCode);

                return new SpeciesMatchResult
                {
                    Status =
                        PerenualLookupStatus.Failed
                };
            }

            var json =
                await response.Content.ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(json))
            {
                return new SpeciesMatchResult
                {
                    Status =
                        PerenualLookupStatus.NoSpecies
                };
            }

            using var document =
                JsonDocument.Parse(json);

            var root = document.RootElement;

            if (!root.TryGetProperty(
                    "data",
                    out var data) ||
                data.ValueKind !=
                    JsonValueKind.Array)
            {
                _logger.LogWarning(
                    "🌱 Perenual species-list response contained no data array.");

                return new SpeciesMatchResult
                {
                    Status =
                        PerenualLookupStatus.NoSpecies
                };
            }

            var candidates =
                data.EnumerateArray().ToList();

            if (candidates.Count == 0)
            {
                return new SpeciesMatchResult
                {
                    Status =
                        PerenualLookupStatus.NoSpecies
                };
            }

            var normalizedQuery =
                plantName.Trim();

            // ----------------------------------------------------
            // EXACT SCIENTIFIC NAME
            // ----------------------------------------------------

            var exactScientific =
                candidates.FirstOrDefault(
                    item =>
                        string.Equals(
                            GetFlexibleString(
                                item,
                                "scientific_name"),
                            normalizedQuery,
                            StringComparison.OrdinalIgnoreCase));

            if (exactScientific.ValueKind !=
                JsonValueKind.Undefined)
            {
                var id =
                    GetFlexibleInt(
                        exactScientific,
                        "id");

                var common =
                    GetFlexibleString(
                        exactScientific,
                        "common_name");

                var scientific =
                    GetFlexibleString(
                        exactScientific,
                        "scientific_name");

                _logger.LogInformation(
                    "🌱 Perenual exact scientific-name match: '{ScientificName}' → ID {Id}",
                    scientific,
                    id);

                return new SpeciesMatchResult
                {
                    Status =
                        id > 0
                            ? PerenualLookupStatus.Success
                            : PerenualLookupStatus.Failed,

                    SpeciesId = id,

                    CommonName = common,

                    ScientificName = scientific
                };
            }

            // ----------------------------------------------------
            // PARTIAL SCIENTIFIC NAME
            // ----------------------------------------------------

            if (LooksLikeScientificName(
                    normalizedQuery))
            {
                var partialScientific =
                    candidates.FirstOrDefault(
                        item =>
                            GetFlexibleString(
                                item,
                                "scientific_name")
                            .Contains(
                                normalizedQuery,
                                StringComparison.OrdinalIgnoreCase));

                if (partialScientific.ValueKind !=
                    JsonValueKind.Undefined)
                {
                    var id =
                        GetFlexibleInt(
                            partialScientific,
                            "id");

                    var common =
                        GetFlexibleString(
                            partialScientific,
                            "common_name");

                    var scientific =
                        GetFlexibleString(
                            partialScientific,
                            "scientific_name");

                    _logger.LogInformation(
                        "🌱 Perenual partial scientific-name match: '{ScientificName}' → ID {Id}",
                        scientific,
                        id);

                    return new SpeciesMatchResult
                    {
                        Status =
                            id > 0
                                ? PerenualLookupStatus.Success
                                : PerenualLookupStatus.Failed,

                        SpeciesId = id,

                        CommonName = common,

                        ScientificName = scientific
                    };
                }

                _logger.LogWarning(
                    "🌱 No reliable scientific-name match found for '{PlantName}'.",
                    plantName);

                return new SpeciesMatchResult
                {
                    Status =
                        PerenualLookupStatus.NoSpecies
                };
            }

            // ----------------------------------------------------
            // EXACT COMMON NAME
            // ----------------------------------------------------

            var exactCommon =
                candidates.FirstOrDefault(
                    item =>
                        string.Equals(
                            GetFlexibleString(
                                item,
                                "common_name"),
                            normalizedQuery,
                            StringComparison.OrdinalIgnoreCase));

            if (exactCommon.ValueKind !=
                JsonValueKind.Undefined)
            {
                var id =
                    GetFlexibleInt(
                        exactCommon,
                        "id");

                var common =
                    GetFlexibleString(
                        exactCommon,
                        "common_name");

                var scientific =
                    GetFlexibleString(
                        exactCommon,
                        "scientific_name");

                _logger.LogInformation(
                    "🌱 Perenual exact common-name match: '{CommonName}' → ID {Id}",
                    common,
                    id);

                return new SpeciesMatchResult
                {
                    Status =
                        id > 0
                            ? PerenualLookupStatus.Success
                            : PerenualLookupStatus.Failed,

                    SpeciesId = id,

                    CommonName = common,

                    ScientificName = scientific
                };
            }

            // ----------------------------------------------------
            // PARTIAL COMMON NAME
            // ----------------------------------------------------

            var partialCommon =
                candidates.FirstOrDefault(
                    item =>
                        GetFlexibleString(
                            item,
                            "common_name")
                        .Contains(
                            normalizedQuery,
                            StringComparison.OrdinalIgnoreCase));

            if (partialCommon.ValueKind !=
                JsonValueKind.Undefined)
            {
                var id =
                    GetFlexibleInt(
                        partialCommon,
                        "id");

                var common =
                    GetFlexibleString(
                        partialCommon,
                        "common_name");

                var scientific =
                    GetFlexibleString(
                        partialCommon,
                        "scientific_name");

                _logger.LogInformation(
                    "🌱 Perenual partial common-name match: '{CommonName}' → ID {Id}",
                    common,
                    id);

                return new SpeciesMatchResult
                {
                    Status =
                        id > 0
                            ? PerenualLookupStatus.Success
                            : PerenualLookupStatus.Failed,

                    SpeciesId = id,

                    CommonName = common,

                    ScientificName = scientific
                };
            }

            // ----------------------------------------------------
            // COMMON NAME FALLBACK
            // ----------------------------------------------------

            var first =
                candidates[0];

            var firstId =
                GetFlexibleInt(
                    first,
                    "id");

            var firstCommon =
                GetFlexibleString(
                    first,
                    "common_name");

            var firstScientific =
                GetFlexibleString(
                    first,
                    "scientific_name");

            if (firstId <= 0)
            {
                return new SpeciesMatchResult
                {
                    Status =
                        PerenualLookupStatus.NoSpecies
                };
            }

            _logger.LogInformation(
                "🌱 Perenual common-name fallback: '{CommonName}' → ID {Id}",
                firstCommon,
                firstId);

            return new SpeciesMatchResult
            {
                Status =
                    PerenualLookupStatus.Success,

                SpeciesId = firstId,

                CommonName = firstCommon,

                ScientificName = firstScientific
            };
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning(
                "🌱 Perenual species-list request timed out.");

            return new SpeciesMatchResult
            {
                Status =
                    PerenualLookupStatus.Failed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "🌱 Perenual species lookup failed.");

            return new SpeciesMatchResult
            {
                Status =
                    PerenualLookupStatus.Failed
            };
        }
    }

    // ============================================================
    // GET SPECIES DETAILS
    // ============================================================

    private async Task<DetailsResult>
        GetSpeciesDetailsAsync(int speciesId)
    {
        try
        {
            _logger.LogInformation(
                "🌱 Perenual requesting details for species ID {SpeciesId}",
                speciesId);

            var apiKey = GetApiKey();

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new DetailsResult
                {
                    Status =
                        PerenualLookupStatus.Failed
                };
            }

            var url =
                $"{BaseUrl}/species/details/{speciesId}" +
                $"?key={Uri.EscapeDataString(apiKey)}";

            using var response =
                await _httpClient.GetAsync(url);

            // FIX: HttpStatusCode comparison
            if (response.StatusCode ==
                HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning(
                    "🌱 Perenual details HTTP 429 for ID {SpeciesId}",
                    speciesId);

                return new DetailsResult
                {
                    Status =
                        PerenualLookupStatus.RateLimited
                };
            }

            // FIX: HttpStatusCode comparison
            if (response.StatusCode ==
                HttpStatusCode.NotFound)
            {
                _logger.LogWarning(
                    "🌱 Perenual details HTTP 404 for ID {SpeciesId}",
                    speciesId);

                return new DetailsResult
                {
                    Status =
                        PerenualLookupStatus.NotFound
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "🌱 Perenual details HTTP {StatusCode} for ID {SpeciesId}",
                    (int)response.StatusCode,
                    speciesId);

                return new DetailsResult
                {
                    Status =
                        PerenualLookupStatus.Failed
                };
            }

            _logger.LogInformation(
                "🌱 Perenual details HTTP 200 for ID {SpeciesId}",
                speciesId);

            var json =
                await response.Content.ReadAsStringAsync();

            _logger.LogInformation(
                "🌱 Perenual details JSON length: {Length}",
                json.Length);

            using var document =
                JsonDocument.Parse(json);

            var root =
                document.RootElement;

            if (root.ValueKind !=
                JsonValueKind.Object)
            {
                return new DetailsResult
                {
                    Status =
                        PerenualLookupStatus.Failed
                };
            }

            var fieldNames =
                root.EnumerateObject()
                    .Select(x => x.Name)
                    .ToList();

            _logger.LogInformation(
                "🌱 Perenual details fields: {Fields}",
                string.Join(
                    ", ",
                    fieldNames));

            // ====================================================
            // RAW FIELD LOGGING
            // ====================================================

            LogRawField(
                root,
                "watering");

            LogRawField(
                root,
                "watering_period");

            LogRawField(
                root,
                "watering_general_benchmark");

            LogRawField(
                root,
                "depth_water_requirement");

            LogRawField(
                root,
                "volume_water_requirement");

            // ====================================================
            // BASIC FIELDS
            // ====================================================

            var plantName =
                GetFlexibleString(
                    root,
                    "common_name");

            var scientificName =
                GetFlexibleString(
                    root,
                    "scientific_name");

            // ====================================================
            // WATERING FIELDS
            // ====================================================

            var watering =
                GetFlexibleString(
                    root,
                    "watering",
                    "water_requirement",
                    "water_requirements",
                    "watering_requirement");

            var wateringPeriod =
                GetFlexibleString(
                    root,
                    "watering_period",
                    "watering_frequency",
                    "water_frequency");

            var depthWaterRequirement =
                GetFlexibleString(
                    root,
                    "depth_water_requirement",
                    "depth_of_water_requirement");

            var volumeWaterRequirement =
                GetFlexibleString(
                    root,
                    "volume_water_requirement",
                    "volume_of_water_requirement",
                    "water_volume_requirement");

            var wateringGeneralBenchmark =
                GetFlexibleString(
                    root,
                    "watering_general_benchmark",
                    "watering_benchmark",
                    "water_general_benchmark");

            // ====================================================
            // OTHER CARE FIELDS
            // ====================================================

            var sunlight =
                GetFlexibleString(
                    root,
                    "sunlight",
                    "sunlight_requirement",
                    "light");

            var soil =
                GetFlexibleString(
                    root,
                    "soil",
                    "soil_requirement",
                    "soil_requirements");

            var pruning =
                GetFlexibleString(
                    root,
                    "pruning",
                    "pruning_requirement",
                    "pruning_requirements");

            var growthRate =
                GetFlexibleString(
                    root,
                    "growth_rate",
                    "growth");

            var careLevel =
                GetFlexibleString(
                    root,
                    "care_level",
                    "care");

            var maintenance =
                GetFlexibleString(
                    root,
                    "maintenance");

            var hardiness =
                GetFlexibleString(
                    root,
                    "hardiness",
                    "hardiness_location");

            // ====================================================
            // LOG EXTRACTED DATA
            // ====================================================

            _logger.LogInformation(
                "🌱 Perenual extracted watering='{Watering}' | period='{Period}' | depth='{Depth}' | volume='{Volume}' | benchmark='{Benchmark}'",
                watering,
                wateringPeriod,
                depthWaterRequirement,
                volumeWaterRequirement,
                wateringGeneralBenchmark);

            // ====================================================
            // BUILD WATERING CONTEXT
            // ====================================================

            var wateringParts =
                new List<string>();

            AddContextPart(
                wateringParts,
                "Watering",
                watering);

            AddContextPart(
                wateringParts,
                "Watering period",
                wateringPeriod);

            AddContextPart(
                wateringParts,
                "Depth water requirement",
                depthWaterRequirement);

            AddContextPart(
                wateringParts,
                "Volume water requirement",
                volumeWaterRequirement);

            AddContextPart(
                wateringParts,
                "General watering benchmark",
                wateringGeneralBenchmark);

            var wateringContext =
                string.Join(
                    "\n",
                    wateringParts);

            // ====================================================
            // BUILD COMPLETE CONTEXT
            // ====================================================

            var context =
                new StringBuilder();

            context.AppendLine(
                $"Plant: {plantName}");

            context.AppendLine(
                $"Scientific name: {scientificName}");

            if (!string.IsNullOrWhiteSpace(
                    wateringContext))
            {
                context.AppendLine();
                context.AppendLine(
                    "WATERING INFORMATION FROM PERENUAL:");

                context.AppendLine(
                    wateringContext);
            }

            AddContextLine(
                context,
                "Sunlight",
                sunlight);

            AddContextLine(
                context,
                "Soil",
                soil);

            AddContextLine(
                context,
                "Pruning",
                pruning);

            AddContextLine(
                context,
                "Growth rate",
                growthRate);

            AddContextLine(
                context,
                "Care level",
                careLevel);

            AddContextLine(
                context,
                "Maintenance",
                maintenance);

            AddContextLine(
                context,
                "Hardiness",
                hardiness);

            var finalContext =
                context.ToString().Trim();

            var result =
                new PerenualCareResult
                {
                    Status =
                        PerenualLookupStatus.Success,

                    PlantName =
                        plantName,

                    ScientificName =
                        scientificName,

                    Watering =
                        wateringContext,

                    Sunlight =
                        sunlight,

                    Soil =
                        soil,

                    Pruning =
                        pruning,

                    GrowthRate =
                        growthRate,

                    CareLevel =
                        careLevel,

                    Maintenance =
                        maintenance,

                    Hardiness =
                        hardiness,

                    Context =
                        finalContext
                };

            if (!result.HasAnyCareData)
            {
                _logger.LogWarning(
                    "🌱 Perenual details returned HTTP 200 but no recognized care fields were found for '{ScientificName}'",
                    scientificName);
            }
            else
            {
                _logger.LogInformation(
                    "🌱 Perenual care data successfully retrieved for '{ScientificName}'",
                    scientificName);
            }

            return new DetailsResult
            {
                Status =
                    PerenualLookupStatus.Success,

                Result =
                    result
            };
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning(
                "🌱 Perenual details request timed out for ID {SpeciesId}",
                speciesId);

            return new DetailsResult
            {
                Status =
                    PerenualLookupStatus.Failed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "🌱 Failed to parse Perenual details for ID {SpeciesId}",
                speciesId);

            return new DetailsResult
            {
                Status =
                    PerenualLookupStatus.Failed
            };
        }
    }

    // ============================================================
    // RAW FIELD LOGGER
    // ============================================================

    private void LogRawField(
        JsonElement root,
        string fieldName)
    {
        if (!root.TryGetProperty(
                fieldName,
                out var value))
        {
            _logger.LogInformation(
                "🌱 Perenual raw {Field}: <missing>",
                fieldName);

            return;
        }

        _logger.LogInformation(
            "🌱 Perenual raw {Field}: {Value}",
            fieldName,
            value.ToString());
    }

    // ============================================================
    // FLEXIBLE STRING
    // ============================================================

    private static string GetFlexibleString(
        JsonElement root,
        params string[] propertyNames)
    {
        foreach (var propertyName
                 in propertyNames)
        {
            if (!root.TryGetProperty(
                    propertyName,
                    out var value))
            {
                continue;
            }

            var extracted =
                ExtractJsonValue(value);

            if (!string.IsNullOrWhiteSpace(
                    extracted))
            {
                return extracted.Trim();
            }
        }

        return string.Empty;
    }

    // ============================================================
    // FLEXIBLE INTEGER
    // ============================================================

    private static int GetFlexibleInt(
        JsonElement root,
        string propertyName)
    {
        if (!root.TryGetProperty(
                propertyName,
                out var value))
        {
            return 0;
        }

        if (value.ValueKind ==
            JsonValueKind.Number)
        {
            if (value.TryGetInt32(
                    out var number))
            {
                return number;
            }
        }

        if (value.ValueKind ==
                JsonValueKind.String &&
            int.TryParse(
                value.GetString(),
                out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    // ============================================================
    // EXTRACT JSON VALUE
    // ============================================================

    private static string ExtractJsonValue(
        JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return value.GetString() ??
                       string.Empty;

            case JsonValueKind.Number:
                return value.ToString();

            case JsonValueKind.True:
                return "true";

            case JsonValueKind.False:
                return "false";

            case JsonValueKind.Array:
            {
                var parts =
                    new List<string>();

                foreach (var item
                         in value.EnumerateArray())
                {
                    var extracted =
                        ExtractJsonValue(item);

                    if (!string.IsNullOrWhiteSpace(
                            extracted))
                    {
                        parts.Add(extracted);
                    }
                }

                return string.Join(
                    ", ",
                    parts);
            }

            case JsonValueKind.Object:
            {
                var parts =
                    new List<string>();

                foreach (var property
                         in value.EnumerateObject())
                {
                    var extracted =
                        ExtractJsonValue(
                            property.Value);

                    if (!string.IsNullOrWhiteSpace(
                            extracted))
                    {
                        parts.Add(
                            $"{property.Name}: {extracted}");
                    }
                }

                return string.Join(
                    "; ",
                    parts);
            }

            default:
                return string.Empty;
        }
    }

    // ============================================================
    // CONTEXT HELPERS
    // ============================================================

    private static void AddContextPart(
        List<string> parts,
        string label,
        string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add(
                $"{label}: {value}");
        }
    }

    private static void AddContextLine(
        StringBuilder builder,
        string label,
        string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.AppendLine(
                $"{label}: {value}");
        }
    }

    // ============================================================
    // API KEY
    // ============================================================

    private string GetApiKey()
    {
        return
            _config["Perenual:ApiKey"] ??
            _config["Perenual:Key"] ??
            Environment.GetEnvironmentVariable(
                "PERENUAL_API_KEY") ??
            string.Empty;
    }

    // ============================================================
    // CLEAN PLANT NAME
    // ============================================================

    private static string CleanPlantName(
        string plantName)
    {
        if (string.IsNullOrWhiteSpace(
                plantName))
        {
            return string.Empty;
        }

        var result =
            plantName.Trim();

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
    // SCIENTIFIC NAME DETECTION
    // ============================================================

    private static bool LooksLikeScientificName(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Regex.IsMatch(
            value.Trim(),
            @"^[A-Z][a-z]{2,}\s+[a-z][a-z-]{2,}$");
    }
}