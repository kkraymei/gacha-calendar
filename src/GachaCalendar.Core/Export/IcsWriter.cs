using System.Security.Cryptography;
using System.Text;

namespace GachaCalendar.Core;

/// <summary>RFC 5545 all-day events, importable by Google, Apple and Outlook calendars.</summary>
public static class IcsWriter
{
    public static string Write(IEnumerable<Entry> entries, IReadOnlyDictionary<string, GameConfig> games)
    {
        var lines = new List<string> { "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//Gacha Calendar//CN", "CALSCALE:GREGORIAN", "METHOD:PUBLISH" };
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
        foreach (var entry in entries.OrderBy(e => e.Start))
        {
            var game = games.GetValueOrDefault(entry.GameId);
            var description = new List<string> { Labels.Kind(entry.Kind) + (entry.IsCollab ? " · 联动" : "") };
            if (entry.Uncertain) description.Add("仅识别到发布日期，活动时间待确认");
            description.AddRange(entry.Origins.Select(o => $"{o.Source}：{o.Url}").Distinct());
            if (entry.Summary.Length > 0) description.Add(Text.Shorten(entry.Summary, 1500));
            lines.Add("BEGIN:VEVENT");
            lines.Add("UID:" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(entry.Key)))[..20] + "@gacha-calendar");
            lines.Add("DTSTAMP:" + stamp);
            lines.Add($"DTSTART;VALUE=DATE:{entry.Start:yyyyMMdd}");
            lines.Add($"DTEND;VALUE=DATE:{entry.End.AddDays(1):yyyyMMdd}");
            lines.Add("SUMMARY:" + Escape((game is null ? "" : $"[{game.Name}] ") + entry.Title));
            lines.Add("DESCRIPTION:" + Escape(string.Join("\n", description)));
            lines.Add("CATEGORIES:" + Escape(Labels.Kind(entry.Kind)));
            if (entry.Url.Length > 0) lines.Add("URL:" + entry.Url);
            lines.Add("END:VEVENT");
        }
        lines.Add("END:VCALENDAR");
        return string.Concat(lines.Select(Fold));
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n");

    /// <summary>Lines longer than 75 octets continue on the next line with a leading space.</summary>
    private static string Fold(string line)
    {
        var builder = new StringBuilder();
        var bytes = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (bytes + size > 75) { builder.Append("\r\n "); bytes = 1; }
            builder.Append(rune.ToString());
            bytes += size;
        }
        return builder.Append("\r\n").ToString();
    }
}

/// <summary>Display names shared by the UI and exports.</summary>
public static class Labels
{
    public static string Kind(EntryKind kind) => kind switch
    {
        EntryKind.Version => "版本",
        EntryKind.Banner => "卡池",
        EntryKind.Event => "活动",
        EntryKind.Offline => "线下",
        EntryKind.Livestream => "直播",
        EntryKind.Maintenance => "维护",
        _ => kind.ToString()
    };
}
