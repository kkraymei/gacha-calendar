using GachaCalendar.Core;

// Read-only live check of the built-in sources:
//   dotnet run --project tools/GachaCalendar.Audit -- [game or game/region ...] [--entries]
Console.OutputEncoding = System.Text.Encoding.UTF8;
var showEntries = args.Contains("--entries");
if (args.Length >= 2 && args[0] == "--debug")
{
    // --debug game/region [title substring]: notices as read, before classification.
    var target = GameCatalog.Create(args[1])!;
    using var client = SourceFetcher.CreateClient();
    foreach (var source in target.Sources)
    {
        var warnings = new List<string>();
        var notices = await new SourceFetcher(client).NoticesAsync(source, new FetchOptions(), warnings);
        Console.WriteLine($"## {source.Name} {notices.Count} notices {string.Join("; ", warnings)}");
        foreach (var n in notices.Where(n => args.Length < 3 || n.Title.Contains(args[2], StringComparison.OrdinalIgnoreCase)).Take(args.Length < 3 ? 40 : 3))
        {
            var body = GachaCalendar.Core.Text.Clean(n.Body, lines: true);
            Console.WriteLine($"- {GachaCalendar.Core.Text.Clean(n.Title)} | pub {n.Published:yyyy-MM-dd} start {n.Start:yyyy-MM-dd} end {n.End:yyyy-MM-dd} fb {n.FallbackEnd:yyyy-MM-dd} | {n.Url}");
            Console.WriteLine("    " + (args.Length < 3 ? body[..Math.Min(160, body.Length)].Replace('\n', '/') : body[..Math.Min(2500, body.Length)]));
        }
    }
    return;
}
var filters = args.Where(a => !a.StartsWith("--")).ToList();
var games = GameCatalog.Games.SelectMany(g => g.Regions.Select(r => $"{g.Id}/{r.Id}"))
    .Where(t => filters.Count == 0 || filters.Any(f => t == f || t.StartsWith(f + "/")))
    .Select(GameCatalog.Create).OfType<GameConfig>().ToList();
using var http = SourceFetcher.CreateClient();
var fetcher = new SourceFetcher(http);
var (entries, reports) = await fetcher.FetchAllAsync(games, new FetchOptions());
foreach (var report in reports.OrderBy(r => r.GameId))
    Console.WriteLine($"{report.State,-8} {report.Count,4}  {report.GameId,-20} {report.Source,-8} {report.Message}");
var raw = reports.Sum(r => r.Count);
Console.WriteLine($"-- {games.Count} servers, {reports.Count} sources, {raw} source entries, {entries.Count} after merging");
if (showEntries)
    foreach (var e in entries.OrderBy(e => e.GameId).ThenBy(e => e.Start))
        Console.WriteLine($"{e.GameId,-18} {Labels.Kind(e.Kind)}{(e.IsCollab ? "+联动" : "")} {e.Start:MM-dd}→{e.End:MM-dd}{(e.Uncertain ? "?" : " ")} [{e.Origins.Count}] {e.Title}{(e.Parent is null ? "" : "  ⟵ " + Text(e.Parent))}");

static string Text(string s) => s.Length > 30 ? s[..30] + "…" : s;
