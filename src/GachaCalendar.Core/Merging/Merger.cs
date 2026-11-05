namespace GachaCalendar.Core;

/// <summary>
/// Merges one notice published through several channels of a game server
/// (in-game board, community, official site, feeds, version digests) while
/// keeping every origin inspectable.
/// </summary>
public static class Merger
{
    // Category tags channels prepend; other bracketed text is a name.
    private const string LeadTag = @"^(?:\s*[【\[](?:公告|预告|通知|官方公告|官方|活动|限时|限时活动|有奖活动|生日活动|资讯|新闻|重要|更新|维护|福利|notice|announcement|news|event|events|update|maintenance|info|summon|campaign|ongoing|updated[^】\]]*|お知らせ|イベント)[】\]])+\s*";
    // Wording that differs between channels for the same notice.
    private const string Boilerplate = @"(?:现已开启|现已开放|现已上线|正式开启|正式上线|即将开启|即将上线|开启预告|上线预告|已开启|开启|开放|上线|来袭|说明|介绍|一览|详情|公告|预告|通知|"
        + @"notice|announcement|details|is\s+now\s+(?:live|available|open)|now\s+(?:live|available|open)|coming\s+soon|开催|开催予告|开催决定|のお知らせ|お知らせ)[\s!！。.~～…]*$";
    private const string Quoted = @"[「『“""《【\[]([^」』”""》】\]]{1,40})[」』”""》】\]]";
    private const string HeadSeparator = @"[：:丨|]|——|—";
    private const string VersionToken = @"(?<![\d.])\d+\.\d+(?:\.\d+)?(?![\d.])";
    private const string PhaseToken = @"第[一二三四五六七八九十\d]+[期部章幕弹轮]|[上下前后]半|其[一二三四五六七八九十]|phase\s*[ivx\d]+|part\s*[ivx\d]+|复刻|rerun";
    private const string GenericKey = @"^(?:活动|公告|更新|维护|版本|event|notice|maintenance|update|banner|version)+$";

    /// <summary>Key: the normalized title. Head: the part before a colon-like separator, which names the notice.</summary>
    private sealed record Identity(string Key, string Core, string[] HeadNames, string HeadResidual,
        string[] Versions, string[] Phases, bool Generic, bool TitledKind);

    public static string TitleKey(string title, string gameName = "") => Describe(title, gameName).Key;

    /// <param name="gameNames">Game id → name, so "《原神》" or a bare "原神" prefix does not tell copies apart.</param>
    public static List<Entry> Merge(IEnumerable<Entry> input, IReadOnlyDictionary<string, string>? gameNames = null)
    {
        var result = new List<Entry>();
        foreach (var group in input.Select(e => (Entry: e, Id: Describe(e.Title, gameNames?.GetValueOrDefault(e.GameId) ?? "")))
                     .GroupBy(x => x.Entry.GameId))
        {
            var clusters = new List<List<(Entry Entry, Identity Id)>>();
            foreach (var item in group.OrderBy(x => x.Entry.Uncertain).ThenBy(x => x.Entry.Start).ThenBy(x => x.Entry.Kind)
                         .ThenBy(x => x.Id.Key, StringComparer.Ordinal).ThenBy(x => x.Entry.Url, StringComparer.Ordinal)
                         .ThenBy(x => x.Entry.Title, StringComparer.Ordinal).ThenBy(x => x.Entry.End)
                         .ThenBy(x => x.Entry.Summary, StringComparer.Ordinal))
            {
                // Every member must match every other, so date tolerances cannot chain separate runs.
                var cluster = clusters.FirstOrDefault(c => c.All(other => SameOccurrence(item.Entry, other.Entry, item.Id, other.Id)));
                if (cluster is null) clusters.Add([item]);
                else cluster.Add(item);
            }
            foreach (var cluster in clusters)
            {
                var members = cluster.Select(x => x.Entry).ToList();
                var chosen = members.OrderBy(e => e.Uncertain).ThenBy(e => e.Parent is not null)
                    .ThenByDescending(e => e.End > e.Start).ThenByDescending(e => e.Summary.Length)
                    .ThenBy(e => e.Url, StringComparer.Ordinal).ThenBy(e => e.End).ThenBy(e => e.Title, StringComparer.Ordinal).First();
                var origins = members.SelectMany(e => e.Origins).Distinct()
                    .OrderBy(o => o.Url, StringComparer.Ordinal).ThenBy(o => o.Start).ThenBy(o => o.End)
                    .ThenBy(o => o.Source, StringComparer.Ordinal).ThenBy(o => o.Uncertain).ToList();
                var dates = origins.Where(o => !o.Uncertain).Select(o => (o.Start, o.End)).Distinct().Count();
                // A kind read from a title outranks one inferred from tags or body.
                var kind = cluster.Where(x => x.Id.TitledKind).Select(x => x.Entry.Kind).DefaultIfEmpty(chosen.Kind).First();
                result.Add(chosen with
                {
                    Kind = kind, Origins = origins, IsCollab = members.Any(e => e.IsCollab),
                    DateConflict = dates > 1 || members.Any(e => e.DateConflict)
                });
            }
        }
        return result.OrderBy(e => e.Start).ThenBy(e => e.GameId, StringComparer.Ordinal).ThenBy(e => e.Title, StringComparer.Ordinal).ToList();
    }

    private static bool SameOccurrence(Entry a, Entry b, Identity ia, Identity ib)
    {
        // "活动"/"维护"-style titles carry no identity; never match them across channels.
        if (ia.Generic || ib.Generic) return a.Kind == b.Kind && a.Title == b.Title && a.Url == b.Url && a.Start == b.Start;
        // Kinds read from both titles must agree; inferred kinds may differ between channels.
        if (a.Kind != b.Kind && ia.TitledKind && ib.TitledKind) return false;
        if (!SameIdentity(ia, ib)) return false;
        var startGap = Math.Abs((a.Start - b.Start).TotalDays);
        if (startGap == 0) return true;
        // A publication-dated copy may precede the event by up to a week; it never extends a known interval.
        if (a.Uncertain || b.Uncertain) return Math.Min(ia.Key.Length, ib.Key.Length) >= 6 && startGap <= 7;
        var endGap = Math.Abs((a.End - b.End).TotalDays);
        // Channels disagree on when a run starts (shown early in-game vs the event), but a shared end identifies it.
        if (endGap == 0 && startGap <= 14 && a.End > a.Start && b.End > b.Start) return true;
        // One day either way covers channels writing dates in different time zones.
        return startGap <= 1 && endGap <= 1;
    }

    private static bool SameIdentity(Identity a, Identity b)
    {
        if (!a.Phases.SequenceEqual(b.Phases)) return false;
        if (a.Versions.Length > 0 && b.Versions.Length > 0 && !a.Versions.SequenceEqual(b.Versions)) return false;
        if (a.Key == b.Key) return true;
        if (a.HeadNames.Length > 0 && a.HeadNames.SequenceEqual(b.HeadNames)) return Overlap(a.HeadResidual, b.HeadResidual) >= 0.6;
        return a.HeadNames.Length == 0 && b.HeadNames.Length == 0 && Math.Min(a.Core.Length, b.Core.Length) >= 6 && Dice(a.Core, b.Core) >= 0.85;
    }

    private static Identity Describe(string title, string gameName)
    {
        var text = Rules.Prepare(title).Trim();
        foreach (var name in GameNames(gameName))
        {
            text = text.Replace($"《{name}》", " ").Replace($"【{name}】", " ").Replace($"[{name}]", " ").Trim();
            if (text.StartsWith(name, StringComparison.Ordinal)) text = text[name.Length..].Trim();
        }
        // A game name in 《》/【】 brackets is packaging; other quoted names identify the notice.
        text = Text.Replace(text, @"^\s*[《【\[][^》】\]]{1,20}[》】\]]\s*(?=\S)(?=.*[「『“""《【\[])", "").Trim();
        text = Text.Replace(text, LeadTag, "").Trim();
        for (var previous = ""; previous != text;)
        {
            previous = text;
            text = Text.Replace(text, Boilerplate, "").Trim();
        }
        var separator = Text.Matches(text, HeadSeparator).FirstOrDefault(m => m.Index > 0);
        var head = separator is null ? text : text[..separator.Index];
        var headNames = Text.Matches(head, Quoted).Select(m => Strip(m.Groups[1].Value))
            .Where(n => n.Length >= 2).Distinct().Order(StringComparer.Ordinal).ToArray();
        var versions = Text.Matches(text, VersionToken).Select(m => m.Value).Distinct().Order(StringComparer.Ordinal).ToArray();
        var phases = Text.Matches(text, PhaseToken).Select(m => NormalizePhase(m.Value)).Distinct().Order(StringComparer.Ordinal).ToArray();
        var key = Strip(text);
        var core = Strip(Remove(Remove(text, VersionToken), PhaseToken));
        var residual = Strip(Remove(Remove(Remove(head, Quoted), VersionToken), PhaseToken));
        var generic = key.Length < 4 || Text.Replace(key, GenericKey, "").Length == 0;
        return new(key, core, headNames, residual, versions, phases, generic, Rules.TitleNamesKind(title));
    }

    // "崩坏：星穹铁道" is also written "星穹铁道"; "胜利女神：NIKKE" as "NIKKE".
    private static IEnumerable<string> GameNames(string gameName)
    {
        var name = Rules.Prepare(Text.Replace(gameName, @"[（(].*?[）)]", "")).Trim();
        if (name.Length == 0) yield break;
        yield return name;
        foreach (var part in name.Split([':', '：'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (part.Length >= 2 && part != name) yield return part;
    }

    private static string Remove(string text, string pattern) => Text.Replace(text, pattern, " ");

    // Punctuation between digits is kept: version 1.23 must not become 12.3.
    private static string Strip(string text) => Text.Replace(text, @"(?<!\d)[\p{P}\p{S}]|[\p{P}\p{S}](?!\d)|\s+", "");

    private static string NormalizePhase(string value)
    {
        const string numerals = "一二三四五六七八九";
        return new string(value.Replace(" ", "").Replace("十", "10").ToLowerInvariant()
            .Select(c => numerals.IndexOf(c) is var i && i >= 0 ? (char)('1' + i) : c).ToArray());
    }

    private static HashSet<string> Bigrams(string text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < text.Length; i++) set.Add(text.Substring(i, 2));
        return set;
    }

    // Share of the shorter text's bigrams found in the longer one.
    private static double Overlap(string x, string y)
    {
        var a = Bigrams(x); var b = Bigrams(y);
        if (a.Count == 0 || b.Count == 0) return 1;
        return (double)a.Intersect(b).Count() / Math.Min(a.Count, b.Count);
    }

    private static double Dice(string x, string y)
    {
        var a = Bigrams(x); var b = Bigrams(y);
        return a.Count + b.Count == 0 ? 1 : 2.0 * a.Intersect(b).Count() / (a.Count + b.Count);
    }
}
