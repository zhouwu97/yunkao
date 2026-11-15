using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using YunKao.Core.Models;

namespace YunKao.Core.Services;

public sealed class DiagnosticLog
{
    private static readonly Regex SecretPattern = new(
        """(?i)(["']?(?:api[_-]?key|authorization|password|passwd|(?:access[_-]?|refresh[_-]?|id[_-]?)?token|session[_-]?id)["']?\s*[:=]\s*)(?:"[^"\r\n]*"|'[^'\r\n]*'|(?:Bearer\s+)?[^,;&\s"'<>]+)""",
        RegexOptions.Compiled);
    private static readonly Regex CookiePattern = new(
        @"(?im)(\b(?:set-cookie|cookie)\s*:\s*)[^\r\n]+",
        RegexOptions.Compiled);
    private readonly object _gate = new();
    private readonly ObservableCollection<DiagnosticRecord> _entries = [];
    private readonly string _logPath;
    private long _nextId;

    public DiagnosticLog(string? logDirectory = null)
    {
        string directory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YunKaoDesktop",
            "logs");
        Directory.CreateDirectory(directory);
        _logPath = Path.Combine(directory, "app.log");
    }

    public event EventHandler<DiagnosticRecord>? EntryAdded;

    public string LogPath => _logPath;
    public string LogDirectory => Path.GetDirectoryName(_logPath) ?? "";

    public IReadOnlyList<DiagnosticRecord> Entries
    {
        get { lock (_gate) return _entries.ToArray(); }
    }

    public void Info(string message) => Add("info", message);
    public void Warning(string message) => Add("warning", message);
    public void Error(string message, Exception? exception = null)
    {
        Add("error", exception is null ? message : $"{message}: {exception.Message}");
    }

    private void Add(string level, string message)
    {
        string safeMessage = Sanitize(message);
        DiagnosticRecord record;
        lock (_gate)
        {
            record = new DiagnosticRecord(++_nextId, DateTimeOffset.Now, level, safeMessage);
            _entries.Add(record);
            while (_entries.Count > 500) _entries.RemoveAt(0);
        }

        try
        {
            File.AppendAllText(
                _logPath,
                JsonSerializer.Serialize(record) + Environment.NewLine);
        }
        catch { }
        EntryAdded?.Invoke(this, record);
    }

    public static string Sanitize(string message)
    {
        string safe = CookiePattern.Replace(message ?? "", "$1[REDACTED]");
        return SecretPattern.Replace(safe, "$1[REDACTED]");
    }
}
