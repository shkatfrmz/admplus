using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Admplus.Api.Services;

public class LogEntry
{
    public string Id { get; set; } = "";
    public DateTimeOffset Time { get; set; }
    public string Level { get; set; } = "info";
    public string Source { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Detail { get; set; }
    public string? Exception { get; set; }
}

public class AppLog
{
    private static readonly Regex Secret = new(
        @"(?i)(""(?:password|clientSecret|secret|unicodePwd)""\s*:\s*"")[^""]*("")|(password|clientSecret|secret)=([^&\s,]+)",
        RegexOptions.Compiled);

    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private readonly object _fileGate = new();
    private const int MaxEntries = 1000;

    public string FilePath { get; }

    public AppLog(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "data", "logs");
        Directory.CreateDirectory(dir);
        FilePath = Path.Combine(dir, $"admplus-{DateTime.UtcNow:yyyyMMdd}.log");
        Write("info", "app", $"Admplus log started. File {FilePath}");
    }

    public void Write(string level, string source, string message, object? detail = null, Exception? ex = null)
    {
        var entry = new LogEntry
        {
            Id = Guid.NewGuid().ToString("n")[..12],
            Time = DateTimeOffset.UtcNow,
            Level = string.IsNullOrWhiteSpace(level) ? "info" : level.ToLowerInvariant(),
            Source = source,
            Message = Redact(message),
            Detail = detail is null ? null : Redact(detail is string s ? s : JsonSerializer.Serialize(detail)),
            Exception = ex is null ? null : Flatten(ex)
        };
        _entries.Enqueue(entry);
        while (_entries.Count > MaxEntries && _entries.TryDequeue(out _)) { }

        var line = $"{entry.Time:O} [{entry.Level.ToUpperInvariant(),-5}] {entry.Source} {entry.Message}";
        if (!string.IsNullOrEmpty(entry.Detail)) line += $" | {entry.Detail}";
        lock (_fileGate)
        {
            File.AppendAllText(FilePath, line + Environment.NewLine);
            if (!string.IsNullOrEmpty(entry.Exception))
                File.AppendAllText(FilePath, entry.Exception + Environment.NewLine);
        }
        Console.WriteLine(line);
        if (!string.IsNullOrEmpty(entry.Exception))
            Console.WriteLine(entry.Exception);
    }

    public List<LogEntry> Query(string? q, string? level, int take)
    {
        take = Math.Clamp(take <= 0 ? 200 : take, 1, 1000);
        IEnumerable<LogEntry> items = _entries.Reverse();
        if (!string.IsNullOrWhiteSpace(level) && !string.Equals(level, "all", StringComparison.OrdinalIgnoreCase))
            items = items.Where(e => e.Level.Equals(level, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var n = q.ToLowerInvariant();
            items = items.Where(e =>
                e.Message.ToLowerInvariant().Contains(n) ||
                e.Source.ToLowerInvariant().Contains(n) ||
                (e.Detail ?? "").ToLowerInvariant().Contains(n) ||
                (e.Exception ?? "").ToLowerInvariant().Contains(n));
        }
        return items.Take(take).ToList();
    }

    private static string Flatten(Exception ex)
    {
        var parts = new List<string>();
        for (var cur = ex; cur != null; cur = cur.InnerException)
            parts.Add($"{cur.GetType().Name}: {Redact(cur.Message)}");
        if (!string.IsNullOrEmpty(ex.StackTrace))
            parts.Add(ex.StackTrace);
        return string.Join(Environment.NewLine, parts);
    }

    private static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return Secret.Replace(text, m =>
            m.Groups[1].Success
                ? m.Groups[1].Value + "********" + m.Groups[2].Value
                : m.Groups[3].Value + "=********");
    }
}
