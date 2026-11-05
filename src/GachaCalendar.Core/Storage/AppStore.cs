using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GachaCalendar.Core;

/// <summary>Per-entry decisions the user makes to set priorities.</summary>
public sealed record EntryMark
{
    public bool Starred { get; init; }
    public bool Done { get; init; }
    public bool Hidden { get; init; }
    public DateTime Updated { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public bool IsEmpty => !Starred && !Done && !Hidden;
}

public sealed record Settings
{
    public List<GameConfig> Games { get; init; } = [];
    public Dictionary<string, EntryMark> Marks { get; init; } = [];
    public int RetentionMonths { get; init; } = 3;
    public int RefreshHours { get; init; } = 6;
    public string Theme { get; init; } = "system";
    public List<EntryKind> HiddenKinds { get; init; } = [];
    public bool ShowDone { get; init; } = true;
    public double DayWidth { get; init; } = 30;
    public string View { get; init; } = "timeline";
    public string Sort { get; init; } = "ending";
    public DateTime? LastRefresh { get; init; }
    public List<SourceReport> Reports { get; init; } = [];
}

/// <summary>Settings, user marks, the last result and per-source caches as JSON files in the app data folder.</summary>
public sealed class AppStore : ISourceCache
{
    private readonly string root;
    private readonly object sync = new();

    public AppStore(string? directory = null)
    {
        root = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GachaCalendar");
        Directory.CreateDirectory(Path.Combine(root, "cache"));
    }

    public string Folder => root;
    private string SettingsPath => Path.Combine(root, "settings.json");
    private string SnapshotPath => Path.Combine(root, "entries.json");

    public Settings LoadSettings()
    {
        var settings = Read<Settings>(SettingsPath);
        if (settings is null || settings.Games.Count == 0) return (settings ?? new Settings()) with { Games = GameCatalog.DefaultGames() };
        // Template games follow catalog updates unless the user edited their sources.
        var games = settings.Games.Select(g => g.Template is { } t && !g.CustomSources && GameCatalog.Create(t) is { } fresh
            ? fresh with { Id = g.Id, Visible = g.Visible } : g).ToList();
        // Marks for entries long gone are pruned.
        var marks = settings.Marks.Where(m => m.Value.Updated > DateTime.Now.AddMonths(-12) && !m.Value.IsEmpty).ToDictionary();
        return settings with { Games = games, Marks = marks };
    }

    public void SaveSettings(Settings settings) => Write(SettingsPath, settings);

    public List<Entry> LoadSnapshot() => Read<List<Entry>>(SnapshotPath) ?? [];

    public void SaveSnapshot(List<Entry> entries) => Write(SnapshotPath, entries);

    public CachedSource? Load(string key) => Read<CachedSource>(CachePath(key));

    public void Save(string key, CachedSource value) => Write(CachePath(key), value);

    private string CachePath(string key) =>
        Path.Combine(root, "cache", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24] + ".json");

    private T? Read<T>(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json.Options) : default; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return default; }
    }

    private void Write<T>(string path, T value)
    {
        lock (sync)
        {
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, Json.Options));
            File.Move(temp, path, overwrite: true);
        }
    }
}
