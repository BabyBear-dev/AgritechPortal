using AgritechPortal.Data;
using AgritechPortal.Services;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.WriteIndented = false;
    });
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ============================================================
// HTTP clients
// ============================================================

builder.Services.AddHttpClient<AiAssistantService>(c =>
{
    c.Timeout = TimeSpan.FromMinutes(2);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("AgritechPortal/1.0 (school project)");
});

builder.Services.AddHttpClient<AIDecisionEngine>(c =>
{
    c.Timeout = TimeSpan.FromMinutes(2);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("AgritechPortal/1.0 (school project)");
});

builder.Services.AddHttpClient<WeatherApiService>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("AgritechPortal/1.0 (school project)");
});

builder.Services.AddHttpClient<WeatherSyncService>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("AgritechPortal/1.0 (school project)");
});

builder.Services.AddHttpClient<PerenualService>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("AgritechPortal/1.0 (school project)");
    c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});

// ============================================================
// Scoped services
// ============================================================

builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<AiAssistantService>();
builder.Services.AddScoped<WeatherSyncService>();
builder.Services.AddScoped<AIDecisionEngine>();
builder.Services.AddScoped<PredictiveHarvestService>();
builder.Services.AddScoped<DataIngestionService>();
builder.Services.AddScoped<WeatherApiService>();
builder.Services.AddScoped<FarmMemoryService>();

// ============================================================
// CORS
// ============================================================

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
            "http://localhost:3000",
            "http://127.0.0.1:5500",
            "http://localhost:5000",
            "http://localhost:5030",
            "https://wizard-sanitary-halved.ngrok-free.dev"
        )
        .AllowAnyHeader()
        .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors("AllowFrontend");
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthorization();
app.MapControllers();

// ============================================================
// 🔥 Warm-up: check Groq + Ollama availability on startup
// ============================================================
_ = Task.Run(async () =>
{
    var groqKey        = app.Configuration["Groq:ApiKey"] ?? "";
    var ollamaOnly     = app.Configuration.GetValue<bool>("Ollama:Preferred", false);
    var searxngUrl     = app.Configuration["SearXNG:BaseUrl"] ?? "http://localhost:8888";
    var searxngEnabled = app.Configuration.GetValue<bool>("SearXNG:Enabled", true);
    var perenualKey    = app.Configuration["Perenual:ApiKey"] ?? "";
    var perenualEnabled = app.Configuration.GetValue<bool>("Perenual:Enabled", true);
    const string groqModel = "openai/gpt-oss-120b";

    const string userAgent = "AgritechPortal/1.0 (school project)";

    Console.WriteLine("─────────────────────────────────────────────");
    Console.WriteLine($"🤖 AI mode: {(ollamaOnly ? "Ollama ONLY" : $"Groq ({groqModel}) → Ollama fallback")}");
    Console.WriteLine($"🌐 Web search: {(searxngEnabled ? $"ENABLED ({searxngUrl})" : "DISABLED")}");
    Console.WriteLine($"🌱 Perenual: {(perenualEnabled && !string.IsNullOrEmpty(perenualKey) ? "ENABLED" : "DISABLED (no key or disabled)")}");
    Console.WriteLine("─────────────────────────────────────────────");

    // --------------------------------------------------------
    // 1. Groq warm-up — only if not in Ollama-only mode
    // --------------------------------------------------------
    if (!ollamaOnly && !string.IsNullOrEmpty(groqKey))
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

            var body = JsonSerializer.Serialize(new
            {
                model = groqModel,
                messages = new[] { new { role = "user", content = "hi" } },
                max_tokens = 5
            });
            var req = new HttpRequestMessage(HttpMethod.Post,
                "https://api.groq.com/openai/v1/chat/completions");
            req.Headers.Add("Authorization", $"Bearer {groqKey}");
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

            var sw = Stopwatch.StartNew();
            var resp = await client.SendAsync(req);
            sw.Stop();

            Console.WriteLine(resp.IsSuccessStatusCode
                ? $"🔥 Groq warm-up OK in {sw.Elapsed.TotalSeconds:F1}s (model: {groqModel})"
                : $"⚠️  Groq warm-up HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️  Groq warm-up failed: {ex.Message}");
        }
    }
    else if (ollamaOnly)
    {
        Console.WriteLine("ℹ️  Groq skipped (Ollama:Preferred = true)");
    }
    else
    {
        Console.WriteLine("⚠️  No Groq API key — will use Ollama only");
    }

    // --------------------------------------------------------
    // 2. Ollama warm-up (loads local model into RAM)
    // --------------------------------------------------------
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

        var body = JsonSerializer.Serialize(new
        {
            model = "llama3.2:1b",
            messages = new[] { new { role = "user", content = "hi" } },
            stream = false
        });

        var sw = Stopwatch.StartNew();
        var resp = await client.PostAsync(
            "http://localhost:11434/api/chat",
            new StringContent(body, Encoding.UTF8, "application/json"));
        sw.Stop();

        Console.WriteLine(resp.IsSuccessStatusCode
            ? $"🔥 Ollama warm-up OK in {sw.Elapsed.TotalSeconds:F1}s (model: llama3.2:1b)"
            : $"⚠️  Ollama warm-up HTTP {(int)resp.StatusCode}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠️  Ollama warm-up skipped: {ex.Message}");
    }

    // --------------------------------------------------------
    // 3. SearXNG check
    // --------------------------------------------------------
    if (searxngEnabled)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

            var resp = await client.GetAsync($"{searxngUrl}/search?q=test&format=json");

            Console.WriteLine(resp.IsSuccessStatusCode
                ? $"🔥 SearXNG warm-up OK ({searxngUrl})"
                : $"⚠️  SearXNG HTTP {(int)resp.StatusCode} — check settings.yml has 'json' in formats");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️  SearXNG unreachable: {ex.Message}");
        }
    }

    // --------------------------------------------------------
    // 4. Perenual check (only if key is present)
    // --------------------------------------------------------
    if (perenualEnabled && !string.IsNullOrEmpty(perenualKey))
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

            var sw = Stopwatch.StartNew();
            var resp = await client.GetAsync(
                $"https://perenual.com/api/species-list?key={perenualKey}&q=tomato");
            sw.Stop();

            Console.WriteLine(resp.IsSuccessStatusCode
                ? $"🔥 Perenual warm-up OK in {sw.Elapsed.TotalSeconds:F1}s"
                : $"⚠️  Perenual HTTP {(int)resp.StatusCode} — check API key or endpoint version");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️  Perenual unreachable: {ex.Message}");
        }
    }

    Console.WriteLine("─────────────────────────────────────────────");
});

app.Run();