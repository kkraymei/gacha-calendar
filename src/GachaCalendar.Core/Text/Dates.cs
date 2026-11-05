using System.Globalization;
using System.Text.RegularExpressions;

namespace GachaCalendar.Core;

/// <summary>Dates, ranges and open-ended schedules written in announcement prose.</summary>
internal static class Dates
{
    private const string Full = @"(?<!\d)(?<y>\d{4})\s*(?:年\s*|[./-])(?<m>\d{1,2})\s*(?:月\s*|[./-])(?<d>\d{1,2})(?:\s*日)?(?!\d)";
    private const string Short = @"(?<![\d.])(?<m>\d{1,2})\s*月\s*(?<d>\d{1,2})(?:\s*日)?";
    private const string Months = @"(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)";
    // English month names, with or without a year: "October 28, 2026", "28th Oct", "Nov 5".
    private const string EnglishMd = @"\b(?<mn>" + Months + @")\.?\s+(?<d>\d{1,2})(?:st|nd|rd|th)?\b(?:,?\s+(?<y>\d{4})\b)?";
    private const string EnglishDm = @"\b(?<d>\d{1,2})(?:st|nd|rd|th)?\s+(?<mn>" + Months + @")\b\.?(?:,?\s+(?<y>\d{4})\b)?";
    internal const string AnyDate = "(?:" + Full + ")|(?:" + Short + ")|(?:" + EnglishMd + ")|(?:" + EnglishDm + ")";

    // Text allowed between the two dates of one range: weekday/time-zone notes,
    // a clock time (10:00, 10点, 3 PM), "after maintenance", then the separator.
    private const string Note = @"(?:\([^)]{0,24}\))";
    private const string Clock = @"(?:\d{1,2}:\d{2}(?::\d{2})?(?:\s*[ap]\.?m\.?)?|\d{1,2}\s*[点时](?:\s*\d{1,2}\s*分)?|\d{1,2}\s*[ap]\.?m\.?)";
    private const string After = @"(?:起|开始|(?:after|post)\s+(?:the\s+)?(?:server\s+)?(?:maintenance|update)|(?:维护|更新|メンテナンス|アップデート)(?:终了|完了|结束)?后|점검\s*후)";
    private const string Separator = @"(?:[-~～〜—–－至到]|to|until|through)";
    private const string Gap = @"^\s*" + Note + @"?\s*" + Clock + @"?\s*(?:[a-z]{2,4}\b)?\s*" + Note + @"?\s*" + After + @"?\s*" + Note + @"?\s*" + Separator + @"\s*$";

    // Schedule labels, matched on folded (simplified) text.
    private const string Labels = @"活动时间|活动期间|活动日期|开放时间|开放期间|寻访时间|祈愿时间|跃迁时间|调频时间|唤取时间|招募时间|颂愿时间|召唤时间|卡池时间|维护时间|开启时间|上线时间|举办时间|演出时间|"
        + @"开催期间|开催日时|实施期间|召唤期间|ピックアップ期间|メンテナンス日时|期间|"
        + @"event\s+(?:duration|period|time|schedule)|(?:banner|wish|warp|signal search|convene)\s+(?:duration|period)|duration|period|"
        + @"이벤트\s*기간|기간";

    // "X版本更新后 ~ 10/20", "即日起至9月30日", "After the Version 7.0 update – 9/21":
    // the start is not written as a date.
    private const string OpenLead = @"(?:(?:版本)?(?:更新|维护|开启|上线|开放|实装)(?:之)?后|即日起|即日|即刻起|现在起|今日起|メンテナンス(?:终了)?后|アップデート后|점검\s*후|\bfrom\s+now\b|\bnow\b"
        + @"|\bafter\s+(?:the\s+)?(?:version\s+)?(?:[""“”][^""“”]{1,30}[""“”]\s*|v?\d+(?:\.\d+)*\s*)?(?:version\s+)?(?:update|maintenance)\b"
        + @"|\b(?:version\s+)?v?\d+\.\d+\s+(?:version\s+)?update\b|\d+\.\d+\s*版本(?:更新|上线|开启))";

    // "2026/11/05 12:00 - 版本更新维护前": the end is the next version, not a date.
    internal const string VersionEnd = @"(?:" + Separator + @")\s*(?:下(?:个|一)?版本|版本(?:更新)?(?:维护)?(?:前|结束)|本版本结束|until\s+the\s+(?:end\s+of\s+(?:the\s+)?(?:current\s+)?version|next\s+version)|次回アップデート|バージョン终了)";

    internal static DateTime? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim();
        if (DateTime.TryParseExact(text, ["yyyyMMdd", "yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd'T'HHmmss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var compact))
            return compact.Date;
        // Only seconds/milliseconds with plausible digit counts are epochs.
        if (text.All(char.IsDigit))
        {
            if (text.Length is not (10 or 13) || !long.TryParse(text, out var stamp)) return null;
            try { return DateTimeOffset.FromUnixTimeSeconds(text.Length == 13 ? stamp / 1000 : stamp).UtcDateTime.Date; }
            catch (ArgumentOutOfRangeException) { return null; }
        }
        text = text.Replace("年", "-").Replace("月", "-").Replace("日", "");
        text = Text.Replace(text, @"(?<=\d)(?:st|nd|rd|th)\b", "");
        text = Text.Replace(text, @"\bSept\b", "Sep");
        // Keep the calendar day as written; the app shows days, not instants.
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date)) return date.Date;
        return null;
    }

    internal static DateTime? Make(int year, int month, int day)
    {
        if (year is < 1 or > 9999 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return null;
        return new DateTime(year, month, day);
    }

    /// <summary>Text after the first schedule label that is followed by a date; otherwise the whole text.</summary>
    private static string LabelTail(string text)
    {
        foreach (Match label in Text.Matches(text, Labels))
        {
            var tail = text[(label.Index + label.Length)..];
            if (Text.Matches(tail[..Math.Min(160, tail.Length)], AnyDate).Count > 0) return tail;
        }
        return text;
    }

    /// <summary>The first date range (or lone date) in the text, preferring labelled schedules.</summary>
    internal static (DateTime Start, DateTime End)? Range(string text, DateTime? reference = null)
    {
        text = LabelTail(Text.Normalize(text));
        var anchor = reference ?? DateTime.Today;
        var tokens = Text.Matches(text, AnyDate).Cast<Match>().ToList();
        for (var i = 0; i + 1 < tokens.Count; i++)
        {
            var left = tokens[i]; var right = tokens[i + 1];
            if (right.Index < left.Index + left.Length) continue;
            var gap = text[(left.Index + left.Length)..right.Index];
            // Times, weekdays and time-zone notes may sit inside a range; dates
            // from separate sentences or sections are never combined.
            if (gap.Length > 90 || !Text.IsMatch(gap, Gap)) continue;
            if (Token(left, anchor) is not { } start) continue;
            if (Token(right, start, nearest: false) is not { } end) continue;
            if (end < start && !right.Groups["y"].Success && !Text.IsMatch(right.Value, @"\d{4}"))
            {
                try { end = end.AddYears(1); } catch (ArgumentOutOfRangeException) { continue; }
            }
            if (end >= start) return (start, end);
        }
        // "6月18日～25日" reuses the left-hand month.
        foreach (Match match in Text.Matches(text, @"(?:(?<y>\d{4})年)?(?<m>\d{1,2})月(?<d>\d{1,2})日?\s*" + Note + @"?\s*" + Clock + @"?\s*[-~～〜—至到]\s*(?<last>\d{1,2})日"))
        {
            if (Token(match, anchor) is { } start && Make(start.Year, start.Month, int.Parse(match.Groups["last"].Value)) is { } end && end >= start)
                return (start, end);
        }
        foreach (var token in tokens)
            if (Token(token, anchor) is { } date) return (date, date);
        return null;
    }

    /// <summary>
    /// End date of a range whose start is not a date — "3.2版本更新后 ~ 10/20",
    /// "即日起至9月30日", "until September 30, 2026". Null when none is written.
    /// </summary>
    internal static DateTime? OpenEnd(string text, DateTime? reference = null)
    {
        text = LabelTail(Text.Normalize(text));
        var head = text[..Math.Min(text.Length, 200)];
        var match = Text.Matches(head, OpenLead + @"\s*" + Note + @"?\s*(?:起)?\s*" + Separator + @"\s*(?<end>" + AnyDate + ")").FirstOrDefault()
            ?? Text.Matches(head, @"(?:until|till|through|ends?\s+on|截止至|截止到|截至|持续至|持续到)\s*(?<end>" + AnyDate + ")").FirstOrDefault();
        if (match is null) return null;
        var end = Text.Matches(match.Groups["end"].Value, AnyDate).FirstOrDefault();
        return end is null ? null : Token(end, reference ?? DateTime.Today);
    }

    /// <summary>True when the text contains a real two-sided range (not a lone date).</summary>
    internal static bool HasRange(string text) => Range(text) is { } r && r.End > r.Start;

    private static readonly string[] MonthNames = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    private static DateTime? Token(Match match, DateTime anchor, bool nearest = true)
    {
        int month;
        if (match.Groups["mn"].Success) month = Array.FindIndex(MonthNames, n => match.Groups["mn"].Value.StartsWith(n, StringComparison.OrdinalIgnoreCase)) + 1;
        else if (match.Groups["m"].Success) month = int.Parse(match.Groups["m"].Value);
        else return Parse(match.Value);
        var explicitYear = match.Groups["y"].Success;
        var year = explicitYear ? int.Parse(match.Groups["y"].Value) : anchor.Year;
        var date = Make(year, month, int.Parse(match.Groups["d"].Value));
        if (date is null || explicitYear || !nearest) return date;
        // Missing years are anchored to publication time (or today), across New Year.
        return new[] { year - 1, year, year + 1 }.Select(y => Make(y, date.Value.Month, date.Value.Day))
            .Where(d => d is not null).OrderBy(d => Math.Abs((d!.Value - anchor).TotalDays)).FirstOrDefault();
    }
}
