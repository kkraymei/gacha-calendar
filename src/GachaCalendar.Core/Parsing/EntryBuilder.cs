namespace GachaCalendar.Core;

/// <summary>Turns notices into calendar entries: filtering, schedule dates, kind, and digest splitting.</summary>
internal static class EntryBuilder
{
    private const int MaxSections = 16;

    internal static IEnumerable<Entry> Build(Notice notice, string gameId, string sourceName, string fallbackUrl, DateTime? today = null)
    {
        var title = Text.Clean(notice.Title);
        var body = Text.Clean(notice.Body, lines: true);
        if (title.Length == 0 || Rules.IsNoise(title) || Rules.IsBareLabel(title)) yield break;
        var titleRelevant = Rules.IsRelevant(title);
        if (!notice.IsSchedule && !titleRelevant && !Rules.IsRelevant(notice.Hint + " " + body[..Math.Min(body.Length, 3000)])) yield break;
        var url = notice.Url ?? fallbackUrl;
        var digest = !notice.IsSchedule && Rules.IsDigest(title);

        // A notice whose schedule is unknown and whose title names nothing schedulable is a teaser or article.
        if (Create(notice, title, body, gameId, sourceName, url, today, digest) is { } main && (titleRelevant || !main.Uncertain || notice.IsSchedule || Rules.HasScheduleLabel(body)))
            yield return main;

        // Version, update and maintenance notes schedule several events and banners in one body.
        if (digest)
            foreach (var section in Sections(body))
            {
                if (Rules.IsNoise(section.Heading)) continue;
                var part = notice with { Title = section.Heading, Body = section.Text, Start = null, End = null, FallbackEnd = null };
                if (Create(part, section.Heading, section.Text, gameId, sourceName, url, today, digest: false) is { Uncertain: false } entry
                    && entry.End > entry.Start && entry.Kind != EntryKind.Maintenance)
                    yield return entry with { Parent = title };
            }
    }

    private static Entry? Create(Notice notice, string title, string body, string gameId, string sourceName, string url, DateTime? today, bool digest)
    {
        var kind = notice.IsSchedule ? Rules.KindOf(title) ?? EntryKind.Event : Rules.Classify(title, notice.Hint, body);
        // A digest's own schedule is stated near its top; later dates belong to the items it lists.
        var text = (digest ? body[..Math.Min(body.Length, 600)] : body) + "\n" + title;
        var anchor = notice.Published ?? today;
        var range = Dates.Range(text, anchor);
        if (range is { } single && single.Start == single.End && single.Start == notice.Published) range = null;
        var openEnd = Dates.OpenEnd(text, anchor);
        if (range is { } only && only.Start == only.End && openEnd is { } until)
        {
            // The lone date closes an open-start range ("更新后 ~ 10/20").
            if (only.Start == until) range = null;
            else if (until > only.Start) range = (only.Start, until);
        }
        var start = notice.Start ?? range?.Start ?? notice.Published;
        var openUsed = notice.End is null && range is null && openEnd is { } close && (start is null || close >= start);
        if (start is null && openEnd is { } lone) start = lone;
        if (start is null) return null;
        var end = notice.End ?? range?.End ?? (openUsed ? openEnd : null) ?? notice.FallbackEnd ?? start.Value;
        if (end < start.Value || (end - start.Value).TotalDays > 366 * 3) end = start.Value;
        // A publication date is flagged for confirmation; "during version 4.6" is the in-game window itself.
        var uncertain = notice.Start is null && range is null && !openUsed && !(notice.FallbackEnd is not null && Rules.SpansVersion(body));
        // A start with "until the next version" leaves the end unknown.
        if (end == start.Value && notice.End is null && Text.IsMatch(Text.Normalize(text), Dates.VersionEnd)) uncertain = true;

        if (kind == EntryKind.Maintenance && !notice.IsSchedule)
        {
            // Maintenance lasts hours; event dates listed in the notice do not extend it.
            if (Dates.Range(title, anchor) is { } named) { start = named.Start; uncertain = false; }
            end = start.Value;
        }

        return new Entry
        {
            GameId = gameId, Title = Text.Shorten(title, 120), Summary = Text.Shorten(body, 4000), Kind = kind,
            IsCollab = Rules.IsCollab(title) || Rules.IsCollab(notice.Hint),
            Start = start.Value.Date, End = end.Date, Uncertain = uncertain, Url = url, Source = sourceName,
            Origins = [new(sourceName, url, start.Value.Date, end.Date, uncertain)]
        };
    }

    internal sealed record Section(string Heading, string Text);

    private const string Marker = @"^\s*(?:〓|【|■|◆|◇|●|★|☆|▌|▶|►|◎|♦|◤|※?\d{1,2}\s*[.．、)）]\s*\S|[一二三四五六七八九十]+、|「|『|\[)";

    /// <summary>
    /// Headed parts of a digest body that carry their own date range. A heading is a short
    /// marked line (〓…〓, 【…】, ■, ◆, 「…」, numbered); a marked line can also hold its own range.
    /// </summary>
    internal static List<Section> Sections(string body)
    {
        var lines = body.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var sections = new List<Section>();
        void Add(string heading, string text)
        {
            if (heading.Length < 2 || sections.Count >= MaxSections || sections.Any(s => s.Heading == heading) || !Dates.HasRange(text)) return;
            sections.Add(new(heading, text));
        }
        var headings = new List<int>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (!Text.IsMatch(lines[i], Marker) || lines[i].EndsWith('：') || lines[i].EndsWith(':')) continue;
            var heading = HeadingText(lines[i]);
            if (Text.IsMatch(heading, Dates.AnyDate))
            {
                // "3.「科研」开启蓝图补完计划「贝亚德」，活动时间11月5日维护后~12月3日维护"
                var cut = Text.Match(heading, @"[，,。；;]?\s*(?:活动时间|活动期间|开放时间|时间|期间|period|duration)?\s*[:：]?\s*(?:" + Dates.AnyDate + ")");
                var name = HeadingText(heading[..cut.Index]);
                if (cut.Success && cut.Index > 0 && IsName(name, false)) Add(name, lines[i]);
                continue;
            }
            if (lines[i].Length <= 60 && IsName(heading, Wrapped(lines[i]))) headings.Add(i);
        }
        for (var h = 0; h < headings.Count; h++)
        {
            var from = headings[h];
            var to = h + 1 < headings.Count ? headings[h + 1] : lines.Count;
            Add(HeadingText(lines[from]), string.Join("\n", lines.Skip(from + 1).Take(Math.Min(to - from - 1, 40))));
        }
        return sections.Count >= 2 ? sections : [];
    }

    /// <summary>A heading that names an item, not a field label, category or a line of dialogue.</summary>
    // "◆全新岛屿开发季◆", "〓活动〓", "【X】": a heading marked on both sides.
    private static bool Wrapped(string line) => Text.IsMatch(line.Trim(), @"^(?:◆.+◆|〓.+〓|★.+★|■.+■|【[^】]+】)$");

    private static bool IsName(string heading, bool wrapped)
    {
        if (heading.Length is < 2 or > 50 || Text.IsMatch(heading, @"[？?~～…。]")) return false;
        // An item heading names something: a schedule keyword or a quoted name.
        if (!wrapped && !Rules.IsRelevant(heading) && !Text.IsMatch(heading, @"[「『【\[“""]")) return false;
        var p = Rules.Prepare(heading).Trim('「', '」', '『', '』', ' ');
        return !Text.IsMatch(p, @"^(?:活动|祈愿|跃迁|开放|开启|参与|奖励|报名|兑换|获取|活动内容|活动说明|活动规则|规则|说明|注意事项|备注|时间|期间|日期|地点|条件|内容|详情|"
            + @"内容新增|新增内容|新活动|新增活动|限时活动|常驻活动|活动一览|内容优化|优化|调整|其他|其它|系统|玩法|新玩法|商店|新角色|新武器|新装备|角色|武器|"
            + @"event (?:period|duration|details|rewards?)|rewards?|duration|period|details|notes?|how to participate|participation requirements?|gameplay|new events?|events?|"
            + @"new content|content|system|others?|optimi[sz]ations?|adjustments?|bug fixes|shop|new characters?|new weapons?|"
            + @"全新活动|改建内容|対応内容|机能更新|継続中のコンテンツ|新要素|終了|予告|新规追加|追加内容|开催期间|期间|报酬|参加条件|イベント|이벤트 기간|보상|이벤트)\s*(?:时间|期间|内容|说明|条件|方式|规则|一览)?\s*[:：]?$");
    }

    private static string HeadingText(string line)
    {
        // Markers repeat ("▶1. 名称"), so strip until stable. Brackets stay unless they wrap the whole line.
        var text = line.Trim();
        for (var previous = ""; previous != text;)
        {
            previous = text;
            text = Text.Replace(text, @"^[\s〓■◆◇●★☆▌▶►◎♦◤※]+|[\s〓■◆◇●★☆▌:：]+$|^\d{1,2}\s*[.．、)）]\s*|^[一二三四五六七八九十]+、", "").Trim();
            if (text.Length > 2 && (text[0], text[^1]) is ('【', '】') or ('[', ']') or ('〔', '〕') && text.IndexOfAny(['】', ']', '〕']) == text.Length - 1)
                text = text[1..^1].Trim();
        }
        return text;
    }
}
