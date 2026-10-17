using AgritechPortal.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace AgritechPortal.Services;

public class FarmMemoryService
{
    private readonly AppDbContext _db;
    private readonly ILogger<FarmMemoryService> _log;

    public FarmMemoryService(AppDbContext db, ILogger<FarmMemoryService> log)
    {
        _db = db;
        _log = log;
    }

    private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
    {
        "hi", "hello", "hey", "thanks", "thank you", "ok", "okay", "cool",
        "test", "testing", "yes", "no", "good morning", "good night", "bye"
    };

    public static bool IsWorthRemembering(string? question, string? answer)
    {
        if (string.IsNullOrWhiteSpace(question) || string.IsNullOrWhiteSpace(answer))
            return false;

        var q = question.Trim();

        if (q.Length < 15) return false;
        if (Noise.Contains(q)) return false;
        if (answer.Trim().Length < 40) return false;

        return true;
    }

    public async Task SaveAsync(
        int zoneId,
        string question,
        string answer,
        int? chatMessageId = null,
        CancellationToken ct = default)
    {
        try
        {
            if (!IsWorthRemembering(question, answer)) return;

            var conn = _db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync(ct);

            await conn.ExecuteAsync(new CommandDefinition(@"
                INSERT INTO farmmemories
                    (zone_id, created_at, observation, decision,
                     outcome, yield_impact, source, chat_message_id)
                VALUES
                    (@ZoneId, now(), @Observation, @Decision,
                     'pending', 'unknown', 'chat', @ChatMessageId)
                ON CONFLICT (chat_message_id) WHERE chat_message_id IS NOT NULL DO NOTHING",
                new
                {
                    ZoneId        = zoneId,
                    Observation   = Compact(question, 300),
                    Decision      = Compact(answer, 800),
                    ChatMessageId = chatMessageId
                },
                cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Farm Memory auto-save failed for zone {ZoneId}", zoneId);
        }
    }

    private static string Compact(string text, int max)
    {
        var t = Regex.Replace(text, @"\s+", " ").Trim();
        t = Regex.Replace(t, @"[*_`#>]", "");

        if (t.Length <= max) return t;

        var slice = t[..max];
        var cut = slice.LastIndexOfAny(new[] { '.', '!', '?' });

        return (cut > max / 2 ? slice[..(cut + 1)] : slice).TrimEnd() + "…";
    }
}