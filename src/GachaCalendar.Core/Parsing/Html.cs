using System.Text.RegularExpressions;

namespace GachaCalendar.Core;

/// <summary>
/// Bounded extraction from public announcement pages. No script is executed;
/// JSON inside script tags is handed to the JSON reader separately.
/// </summary>
internal static class Html
{
    internal sealed record Article(string Title, string Body, string? Published, string Url);

    /// <summary>List items (article/li/anchor+date) or, for a detail page, its single main article.</summary>
    internal static List<Article> Articles(string html, string pageUrl)
    {
        var found = new List<Article>();
        foreach (Match block in Text.Matches(html, @"<(article|li)\b[^>]*>([\s\S]*?)</\1>"))
        {
            var inner = block.Groups[2].Value;
            var anchor = Text.Matches(inner, @"<a\b([^>]*)>([\s\S]*?)</a>").Cast<Match>()
                .FirstOrDefault(m => Rules.IsRelevant(Text.Clean(m.Groups[2].Value)));
            var heading = Text.Match(inner, @"<h[1-4]\b[^>]*>([\s\S]*?)</h[1-4]>");
            var titled = Text.Match(inner, @"<(?:p|div|span)\b[^>]*class=""[^""]*title[^""]*""[^>]*>([\s\S]*?)</(?:p|div|span)>");
            var title = heading.Success ? Text.Clean(heading.Groups[1].Value)
                : titled.Success ? Text.Clean(titled.Groups[1].Value)
                : anchor is not null ? Text.Clean(anchor.Groups[2].Value) : "";
            if (title.Length < 4 || !Rules.IsRelevant(title)) continue;
            anchor ??= Text.Match(inner, @"<a\b([^>]*)>");
            var time = Text.Match(inner, @"<time\b([^>]*)>([\s\S]*?)</time>");
            var published = time.Success ? Text.Attribute(time.Groups[1].Value, "datetime") ?? Text.Clean(time.Groups[2].Value) : DateText(inner);
            var href = anchor.Success ? Text.Attribute(anchor.Groups[1].Value, "href") : null;
            found.Add(new(title, Text.Clean(inner, lines: true), published, Text.Url(href, pageUrl) ?? pageUrl));
        }
        // Rows like <a href><div class=date>..</div><div class=title>..</div></a>.
        foreach (Match anchor in Text.Matches(html, @"<a\b([^>]*)>([\s\S]{0,1200}?)</a>"))
        {
            var inner = anchor.Groups[2].Value;
            var titled = Text.Match(inner, @"class=""[^""]*title[^""]*""[^>]*>([\s\S]*?)</");
            if (!titled.Success) continue;
            var title = Text.Clean(titled.Groups[1].Value);
            var url = Text.Url(Text.Attribute(anchor.Groups[1].Value, "href"), pageUrl);
            if (title.Length < 4 || url is null || !Rules.IsRelevant(title) || found.Any(a => a.Url == url)) continue;
            found.Add(new(title, Text.Clean(inner, lines: true), DateText(inner), url));
        }
        if (found.Count > 0) return found;
        if (Single(html, pageUrl) is { } article) found.Add(article);
        foreach (Match pair in Text.Matches(html, @"<a\b([^>]*)>([\s\S]*?)</a>\s*<(p|time|span|div)\b([^>]*)>([\s\S]{0,2400}?)</\3>"))
        {
            var title = Text.Clean(pair.Groups[2].Value);
            if (!Rules.IsRelevant(title) || found.Count > 0 && found[0].Url == pageUrl) continue;
            found.Add(new(title, Text.Clean(pair.Groups[5].Value, lines: true),
                pair.Groups[3].Value.Equals("time", StringComparison.OrdinalIgnoreCase) ? Text.Attribute(pair.Groups[4].Value, "datetime") ?? Text.Clean(pair.Groups[5].Value) : null,
                Text.Url(Text.Attribute(pair.Groups[1].Value, "href"), pageUrl) ?? pageUrl));
        }
        return found;
    }

    /// <summary>A detail page: its primary heading and main text.</summary>
    internal static Article? Single(string html, string pageUrl)
    {
        // One primary heading; dates from navigation or footers are not associated with link text.
        var h1 = Text.Match(html, @"<h1\b[^>]*>([\s\S]*?)</h1>");
        var title = h1.Success ? Text.Clean(h1.Groups[1].Value) : null;
        string? published = null;
        foreach (Match meta in Text.Matches(html, @"<meta\b[^>]*>"))
        {
            var property = Text.Attribute(meta.Value, "property") ?? Text.Attribute(meta.Value, "name");
            if (property == "og:title" && string.IsNullOrWhiteSpace(title)) title = Text.Attribute(meta.Value, "content");
            if (property is "article:published_time" or "date" or "datePublished") published = Text.Attribute(meta.Value, "content");
        }
        if (string.IsNullOrWhiteSpace(title))
        {
            var head = Text.Match(html, @"<title\b[^>]*>([\s\S]*?)</title>");
            title = head.Success ? Text.Clean(head.Groups[1].Value) : null;
        }
        if (string.IsNullOrWhiteSpace(title)) return null;
        var main = Text.Match(html, @"<(main|article)\b[^>]*>([\s\S]*)</\1>");
        var body = Text.Clean(main.Success ? main.Groups[2].Value : Text.Replace(html, @"<(?:nav|header|footer)\b[\s\S]*?</(?:nav|header|footer)>", " "), lines: true);
        return new(Text.Clean(title), body, published ?? DateText(html[..Math.Min(html.Length, 20000)]), pageUrl);
    }

    /// <summary>Same-site announcement and feed links a list page offers.</summary>
    internal static List<string> Links(string html, string pageUrl)
    {
        var urls = new List<string>();
        foreach (Match tag in Text.Matches(html, @"<link\b[^>]*>"))
        {
            var type = Text.Attribute(tag.Value, "type") ?? "";
            if (type.Contains("rss") || type.Contains("atom") || type.Contains("calendar")) Add(Text.Attribute(tag.Value, "href"));
        }
        foreach (Match anchor in Text.Matches(html, @"<a\b([^>]*)>([\s\S]*?)</a>"))
        {
            var text = Text.Clean(anchor.Groups[2].Value);
            if (Rules.IsRelevant(text) && !Rules.IsNoise(text)) Add(Text.Attribute(anchor.Groups[1].Value, "href"));
        }
        return urls;

        void Add(string? raw)
        {
            if (Text.Url(raw, pageUrl) is not { } value || !SameSite(value, pageUrl)) return;
            var normalized = new UriBuilder(value) { Fragment = "" }.Uri.AbsoluteUri;
            if (normalized != pageUrl && !urls.Contains(normalized)) urls.Add(normalized);
        }
    }

    internal static bool SameSite(string url, string other) =>
        Uri.TryCreate(url, UriKind.Absolute, out var a) && Uri.TryCreate(other, UriKind.Absolute, out var b)
        && a.GetLeftPart(UriPartial.Authority).Equals(b.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);

    private static string? DateText(string html)
    {
        var date = Text.Match(html, @"class=""[^""]*date[^""]*""[^>]*>([\s\S]{0,60}?)</");
        if (date.Success) return Text.Clean(date.Groups[1].Value).Replace('.', '-');
        var plain = Text.Match(Text.Clean(html), @"\b\d{4}[./-]\d{1,2}[./-]\d{1,2}\b");
        return plain.Success ? plain.Value.Replace('.', '-') : null;
    }
}
