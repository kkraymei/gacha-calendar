using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace GachaCalendar.Core;

/// <summary>An announcement as read from a response, before classification.</summary>
internal sealed record Notice
{
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public string? Url { get; init; }
    public DateTime? Published { get; init; }
    public DateTime? Start { get; init; }
    public DateTime? End { get; init; }
    /// <summary>End of an in-game display window; used only when the body names no end.</summary>
    public DateTime? FallbackEnd { get; init; }
    /// <summary>Category or tag text from the source; helps relevance and kind, never shown.</summary>
    public string Hint { get; init; } = "";
    /// <summary>Calendar feeds schedule by definition and need no announcement keywords.</summary>
    public bool IsSchedule { get; init; }
    /// <summary>Scalar fields of the JSON row, for request and page placeholders.</summary>
    public IReadOnlyDictionary<string, string>? Fields { get; init; }
}

/// <summary>Reads notices from JSON, HTML (with embedded data), RSS/Atom and ICS responses by sniffing content.</summary>
internal static class NoticeReader
{
    private static readonly string[] TitleKeys = ["title", "subject", "articleTitle", "sTitle", "headline", "name"];
    private static readonly string[] BodyKeys = ["content", "articleContent", "sContent", "body", "content_part", "description", "subtitle", "desc", "brief", "articleDesc", "sIntro", "abstract", "summary"];
    private static readonly string[] StartKeys = ["startDate", "start", "begin", "startTime", "start_time", "beginTime", "dtStartTime", "eventStart"];
    private static readonly string[] PublishKeys = ["datePublished", "publishTime", "publish_time", "published_at", "pub_timestamp", "pubDate", "createdAt", "created_at", "createTime", "ctime", "created", "dtCreateTime", "displayTime", "date", "timestamp"];
    private static readonly string[] EndKeys = ["endDate", "end", "finish", "endTime", "end_time", "dtEndTime", "expires", "eventEnd"];
    private static readonly string[] UrlKeys = ["url", "link", "href", "sUrl", "articleUrl", "webUrl"];
    private static readonly string[] HintKeys = ["tag_label", "type_label", "category", "categoryName", "cateName", "typeName", "tagName", "tag", "label", "column", "columnName", "channelName", "group"];

    internal static List<Notice> Read(string text, string contentType, string url, bool startIsPublished = false, DateTime? today = null)
    {
        text = text.TrimStart('﻿', ' ', '\t', '\r', '\n');
        if (text.StartsWith("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase) || contentType.Contains("text/calendar", StringComparison.OrdinalIgnoreCase))
            return Ics(text, url).ToList();
        if (text.StartsWith('{') || text.StartsWith('['))
            return Json(text, url, startIsPublished, today).ToList();
        if (Text.IsMatch(text[..Math.Min(text.Length, 400)], @"^(?:<\?xml[^>]*>\s*)?<(?:rss|feed)\b"))
            return Feed(text, url).ToList();
        return HtmlPage(text, url, startIsPublished, today).ToList();
    }

    // ---- JSON ----

    internal static IEnumerable<Notice> Json(string text, string url, bool startIsPublished, DateTime? today)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 96, AllowTrailingCommas = true }); }
        catch (JsonException) { yield break; }
        using (doc)
        {
            foreach (var row in Walk(doc.RootElement))
                if (FromRow(row, url, startIsPublished, today) is { } notice) yield return notice;
        }
    }

    private static Notice? FromRow(JsonElement row, string sourceUrl, bool startIsPublished, DateTime? today)
    {
        var title = Value(row, TitleKeys) ?? ValueWhere(row, k => k.EndsWith("title", StringComparison.Ordinal) && !k.EndsWith("subtitle", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(title) || title.Length > 300) return null;
        var body = Value(row, BodyKeys) ?? "";
        // Quill-style rich text is stored as a JSON string by some forums.
        if (Value(row, ["structured_content"]) is { Length: > 0 } structured)
        {
            try
            {
                using var rich = JsonDocument.Parse(structured);
                var inserts = string.Concat(Walk(rich.RootElement).Select(n => Value(n, ["insert"])).Where(s => s is not null));
                if (inserts.Length > body.Length) body = inserts;
            }
            catch (JsonException) { }
        }
        var link = Text.Url(Value(row, UrlKeys) ?? ValueWhere(row, IsLinkKey), sourceUrl);
        var published = FirstDate(row, PublishKeys);
        var start = FirstDate(row, StartKeys);
        var end = FirstDate(row, EndKeys);
        if (published is null && start is null && end is null) (start, end, published) = GuessDates(row);
        DateTime? fallbackEnd = null;
        if (startIsPublished)
        {
            // These fields schedule publication or in-game display, not the event;
            // a display window's end is only a fallback for the body.
            published = start ?? published;
            start = FirstDate(row, ["eventStart", "startDate"]);
            if (end is { } windowEnd && published is { } shown && (windowEnd - shown).TotalDays <= 120) fallbackEnd = windowEnd;
            end = FirstDate(row, ["eventEnd", "endDate"]);
        }
        if (published is null && int.TryParse(Value(row, ["month"]), out var month) && int.TryParse(Value(row, ["day"]), out var day))
        {
            var now = today ?? DateTime.Today;
            published = Dates.Make(now.Year, month, day);
            if (published > now.AddMonths(3)) published = Dates.Make(now.Year - 1, month, day);
        }
        var hint = string.Join(" ", HintKeys.Select(k => Value(row, [k])).Where(v => v is not null && !v.All(char.IsDigit)));
        return new Notice
        {
            Title = title, Body = body, Url = link, Published = published, Start = start, End = end,
            FallbackEnd = fallbackEnd, Hint = hint, Fields = Scalars(row)
        };
    }

    private static Dictionary<string, string> Scalars(JsonElement row)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in row.EnumerateObject())
            if (property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                fields.TryAdd(property.Name, property.Value.ToString());
        return fields;
    }

    private static DateTime? FirstDate(JsonElement row, string[] keys)
    {
        foreach (var key in keys)
            if (Dates.Parse(Value(row, [key])) is { } date) return date;
        return null;
    }

    private static string NormalizeKey(string value) => value.Replace("_", "").Replace("-", "").ToLowerInvariant();

    private static bool IsLinkKey(string key) =>
        (key.EndsWith("url", StringComparison.Ordinal) || key.EndsWith("link", StringComparison.Ordinal) || key.EndsWith("href", StringComparison.Ordinal))
        && !Text.IsMatch(key, "img|image|pic|banner|cover|icon|avatar|thumb|video|audio|poster|background|share|qc|jump");

    private static string? Value(JsonElement row, string[] keys)
    {
        if (row.ValueKind != JsonValueKind.Object) return null;
        foreach (var key in keys)
        {
            var wanted = NormalizeKey(key);
            foreach (var property in row.EnumerateObject())
                if (NormalizeKey(property.Name) == wanted && Scalar(property.Value) is { } value) return value;
        }
        return null;
    }

    /// <summary>First scalar whose normalized name passes the filter (CMS naming variants).</summary>
    private static string? ValueWhere(JsonElement row, Func<string, bool> accept)
    {
        if (row.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in row.EnumerateObject())
            if (accept(NormalizeKey(property.Name)) && Scalar(property.Value) is { } value) return value;
        return null;
    }

    private static string? Scalar(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("rendered", out var rendered)) value = rendered;
        if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number)) return null;
        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>Dates from unfamiliar field names (noticeStartTime, online_at): the name decides the role.</summary>
    private static (DateTime? Start, DateTime? End, DateTime? Published) GuessDates(JsonElement row)
    {
        DateTime? start = null, end = null, published = null;
        foreach (var property in row.EnumerateObject())
        {
            var key = NormalizeKey(property.Name);
            if (!(key.EndsWith("time") || key.EndsWith("date") || key.EndsWith("at") || key.EndsWith("timestamp"))) continue;
            if (Dates.Parse(Scalar(property.Value)) is not { } date) continue;
            if (Text.IsMatch(key, "start|begin|open|from")) start ??= date;
            else if (Text.IsMatch(key, "end|finish|close|expire|deadline|until")) end ??= date;
            else if (!Text.IsMatch(key, "update|modif|mtime")) published ??= date;
        }
        return (start, end, published);
    }

    internal static IEnumerable<JsonElement> Walk(JsonElement node, int depth = 0)
    {
        if (depth > 64) yield break;
        if (node.ValueKind == JsonValueKind.Object)
        {
            yield return node;
            foreach (var property in node.EnumerateObject())
                foreach (var child in Walk(property.Value, depth + 1)) yield return child;
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray())
                foreach (var child in Walk(item, depth + 1)) yield return child;
        }
    }

    // ---- HTML ----

    private static IEnumerable<Notice> HtmlPage(string html, string url, bool startIsPublished, DateTime? today)
    {
        foreach (Match script in Text.Matches(html, @"<script\b[^>]*>([\s\S]*?)</script>"))
        {
            var json = script.Groups[1].Value.Trim();
            if (json.StartsWith('{') || json.StartsWith('['))
            {
                foreach (var notice in Json(json, url, startIsPublished, today)) yield return notice;
                continue;
            }
            foreach (Match fragment in Text.Matches(json, @"\{[^{}]{0,16000}""(?:title|subject|articleTitle|sTitle)""[^{}]{0,16000}\}"))
                foreach (var notice in Json(fragment.Value, url, startIsPublished, today)) yield return notice;
            foreach (var payload in FlightPayloads(json))
                foreach (var notice in Json(payload, url, startIsPublished, today)) yield return notice;
        }
        foreach (var article in Html.Articles(html, url))
            yield return new Notice { Title = article.Title, Body = article.Body, Url = article.Url, Published = Dates.Parse(article.Published) };
    }

    /// <summary>Next.js App Router flight records carry page data as JSON lines inside push() calls.</summary>
    private static IEnumerable<string> FlightPayloads(string script)
    {
        foreach (Match packet in Text.Matches(script, @"self\.__next_f\.push\((\[[\s\S]*?\])\)"))
        {
            string? payload = null;
            try
            {
                using var doc = JsonDocument.Parse(packet.Groups[1].Value);
                if (doc.RootElement.GetArrayLength() > 1 && doc.RootElement[1].ValueKind == JsonValueKind.String)
                    payload = doc.RootElement[1].GetString();
            }
            catch (JsonException) { }
            if (payload is null) continue;
            foreach (var line in payload.Split('\n'))
            {
                var colon = line.IndexOf(':');
                if (colon < 0) continue;
                var row = line[(colon + 1)..].Trim();
                if (row.StartsWith('{') || row.StartsWith('[')) yield return row;
            }
        }
    }

    // ---- RSS / Atom ----

    private static IEnumerable<Notice> Feed(string text, string url)
    {
        XDocument doc;
        try
        {
            using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8_000_000 });
            doc = XDocument.Load(reader);
        }
        catch (XmlException) { yield break; }
        foreach (var item in doc.Descendants().Where(n => n.Name.LocalName is "item" or "entry"))
        {
            string? Field(params string[] keys) => keys.Select(k => item.Elements().FirstOrDefault(n => n.Name.LocalName == k)?.Value)
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            var link = item.Elements().FirstOrDefault(n => n.Name.LocalName == "link" && ((string?)n.Attribute("rel") is null or "alternate"));
            yield return new Notice
            {
                Title = Field("title") ?? "",
                Body = Field("encoded", "content", "description", "summary") ?? "",
                Url = Text.Url((string?)link?.Attribute("href") ?? link?.Value, url),
                Published = Dates.Parse(Field("pubDate", "published", "updated", "date")),
                Hint = string.Join(" ", item.Elements().Where(n => n.Name.LocalName == "category").Select(n => n.Value))
            };
        }
    }

    // ---- ICS ----

    private static IEnumerable<Notice> Ics(string text, string url)
    {
        text = Text.Replace(text, @"\r?\n[ \t]", "");
        foreach (Match match in Text.Matches(text, @"BEGIN:VEVENT\s*([\s\S]*?)END:VEVENT"))
        {
            var block = match.Groups[1].Value;
            string? Value(string key)
            {
                var line = block.Split('\n').Select(l => l.TrimEnd('\r')).FirstOrDefault(l =>
                    l.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase) || l.StartsWith(key + ";", StringComparison.OrdinalIgnoreCase));
                return line is null ? null : Text.Rx(@"\\([nN,;\\])").Replace(line[(line.IndexOf(':') + 1)..],
                    m => m.Groups[1].Value is "n" or "N" ? "\n" : m.Groups[1].Value);
            }
            var rawStart = Value("DTSTART");
            if (Dates.Parse(rawStart) is not { } start) continue;
            var rawEnd = Value("DTEND");
            var end = Dates.Parse(rawEnd) ?? start;
            // RFC 5545: DTEND is exclusive for all-day events and midnight ends.
            if (end > start && ((rawStart?.Length == 8 && rawEnd?.Length == 8) || Text.IsMatch(rawEnd ?? "", @"T000000Z?$"))) end = end.AddDays(-1);
            yield return new Notice
            {
                Title = Value("SUMMARY") ?? "未命名日程", Body = Value("DESCRIPTION") ?? "", Url = Text.Url(Value("URL"), url),
                Start = start, End = end < start ? start : end, IsSchedule = true
            };
        }
    }
}
