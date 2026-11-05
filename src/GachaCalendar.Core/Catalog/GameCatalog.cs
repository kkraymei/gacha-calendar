using System.Text.Json;

namespace GachaCalendar.Core;

/// <summary>Built-in games and their regional servers, loaded from the embedded games.json.</summary>
public static class GameCatalog
{
    private sealed record CatalogFile(int Version, List<string> Defaults, List<GameTemplate> Games);

    private static readonly Lazy<CatalogFile> File = new(() =>
    {
        using var stream = typeof(GameCatalog).Assembly.GetManifestResourceStream("GachaCalendar.games.json")
            ?? throw new InvalidOperationException("games.json is not embedded.");
        return JsonSerializer.Deserialize<CatalogFile>(stream, Json.Options) ?? throw new InvalidOperationException("games.json is empty.");
    });

    public static IReadOnlyList<GameTemplate> Games => File.Value.Games;

    public static IReadOnlyList<string> Defaults => File.Value.Defaults;

    public static GameConfig? Create(string template)
    {
        var parts = template.Split('/', 2);
        var game = Games.FirstOrDefault(g => g.Id == parts[0]);
        var region = game?.Regions.FirstOrDefault(r => parts.Length > 1 && r.Id == parts[1]);
        return game is null || region is null ? null : new GameConfig
        {
            Id = $"{game.Id}-{region.Id}",
            Name = game.Name,
            Region = region.Name,
            Publisher = game.Publisher,
            Color = game.Color,
            Sources = region.Sources,
            Template = template
        };
    }

    public static List<GameConfig> DefaultGames() => Defaults.Select(Create).OfType<GameConfig>().ToList();
}

internal static class Json
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
}
