using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace GachaCalendar.Core;

/// <summary>Shared text helpers: cached regexes, HTML cleanup and script folding.</summary>
internal static class Text
{
    private static readonly ConcurrentDictionary<string, Regex> Cache = new();

    internal static Regex Rx(string pattern) => Cache.GetOrAdd(pattern, p => new Regex(p,
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)));

    internal static MatchCollection Matches(string text, string pattern) => Rx(pattern).Matches(text);
    internal static Match Match(string text, string pattern) => Rx(pattern).Match(text);
    internal static bool IsMatch(string text, string pattern) => Rx(pattern).IsMatch(text);
    internal static string Replace(string text, string pattern, string value) => Rx(pattern).Replace(text, value);

    // Traditional → simplified for characters used by keywords and labels, so
    // Taiwan/Hong Kong announcements match the same rules. 喚 is deliberately
    // absent: Japanese 召喚 marks a banner while Chinese 召唤 is also a game mode.
    internal const string Traditional = "動願躍遷補給尋訪調頻維護時復劇關簽錄獎勵別節線會華樂慶機說説頌邊週辦紙掛飾裝膚觀聯閃艦貓東寶預開販發現貨貼頭態聞畫話題轉徵獲攝創賀圖問饋優處罰違規運營間覽戲彈啟紹詳來襲將資訊們際門場氣實體歡鐘點後與為狀況僅參賽權級區國電視遊戰擊殺隊員對應盡專屬購紀號隨種獻證換兌內單選擇測試經驗長買賣價幣讓當這過還從誌類層傳籤衝";
    internal const string Simplified  = "动愿跃迁补给寻访调频维护时复剧关签录奖励别节线会华乐庆机说说颂边周办纸挂饰装肤观联闪舰猫东宝预开贩发现货贴头态闻画话题转征获摄创贺图问馈优处罚违规运营间览戏弹启绍详来袭将资讯们际门场气实体欢钟点后与为状况仅参赛权级区国电视游战击杀队员对应尽专属购纪号随种献证换兑内单选择测试经验长买卖价币让当这过还从志类层传签冲";
    private static readonly Dictionary<char, char> FoldMap = BuildFold();

    private static Dictionary<char, char> BuildFold()
    {
        var map = new Dictionary<char, char>();
        for (var i = 0; i < Traditional.Length; i++) map.TryAdd(Traditional[i], Simplified[i]);
        return map;
    }

    internal static string Fold(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (FoldMap.TryGetValue(chars[i], out var simple)) chars[i] = simple;
        return new string(chars);
    }

    /// <summary>Width/compatibility forms unified, traditional folded, Korean date units mapped to CJK ones.</summary>
    internal static string Normalize(string text) =>
        Fold(text.Normalize(NormalizationForm.FormKC)).Replace("년", "年").Replace("월", "月").Replace("일", "日");

    /// <summary>Plain text from HTML or escaped API text. Block boundaries become line breaks when <paramref name="lines"/> is set.</summary>
    internal static string Clean(string? text, bool lines = false)
    {
        if (string.IsNullOrEmpty(text)) return "";
        // Literal "\n" sequences come from APIs that double-escape line breaks.
        text = Replace(text, @"\\[nr]", "\n");
        text = Replace(text, @"<script\b[^>]*>[\s\S]*?</script>|<style\b[^>]*>[\s\S]*?</style>|<head\b[^>]*>[\s\S]*?</head>", " ");
        for (var pass = 0; pass < 2; pass++)
        {
            text = Replace(text, @"<br\s*/?>|</(?:p|div|li|h[1-6]|tr|section|article|blockquote|dd|dt)>", "\n");
            text = Replace(text, @"<[^>]+>", " ");
            text = WebUtility.HtmlDecode(text);
            // Some CMSs store inline markup entity-escaped (&lt;t&gt;); decoding reveals it.
            if (!text.Contains('<')) break;
            text = Replace(text, @"<(?![a-z/!])", "‹");
        }
        text = text.Replace('\u00A0', ' ').Replace("\u200B", "").Replace('\t', ' ');
        if (!lines) return Replace(text, @"\s+", " ").Trim();
        text = Replace(text, @"[ \f\v]+", " ");
        text = Replace(text, @" *\r?\n *", "\n");
        return Replace(text, @"\n{3,}", "\n\n").Trim();
    }

    internal static string? Attribute(string tag, string name)
    {
        var match = Match(tag, $"(?:^|\\s){Regex.Escape(name)}\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))");
        if (!match.Success) return null;
        return WebUtility.HtmlDecode(match.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value);
    }

    /// <summary>Absolute http(s) URL resolved against a base, or null.</summary>
    internal static string? Url(string? value, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith('#')) return null;
        value = WebUtility.HtmlDecode(value.Trim());
        if (value.StartsWith("//")) value = "https:" + value;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var root)) return null;
        return Uri.TryCreate(root, value, out var url) && url.Scheme is "http" or "https" ? url.AbsoluteUri : null;
    }

    internal static string Shorten(string text, int length) => text.Length <= length ? text : text[..length].TrimEnd() + "…";
}
