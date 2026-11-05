using System.Text.RegularExpressions;

namespace GachaCalendar.Core;

/// <summary>
/// Keyword rules that decide whether a notice belongs on the calendar and what
/// it schedules. Words are stored folded (simplified) and matched against folded
/// text, so traditional-Chinese and Japanese kanji notices share the rules.
/// English words match whole words with inflections (events, headhunting) but
/// never substrings (restored ≠ store).
/// </summary>
internal static class Rules
{
    private static readonly string[] Maintenance = Folded("维护", "停服", "停机", "maintenance", "downtime", "メンテナンス", "점검");

    private static readonly string[] Livestream = Folded("前瞻", "特别节目", "直播", "special program", "livestream", "live stream", "version preview",
        "生放送", "生配信", "생방송");

    // Offline events: venues, conventions, concerts, pop-up stores and themed cafés.
    private static readonly string[] Offline = Folded("线下", "快闪", "主题店", "主题餐厅", "主题咖啡", "咖啡厅", "展会", "漫展", "嘉年华", "演唱会", "音乐会",
        "见面会", "巡展", "展览", "展出", "主题展", "地铁", "bilibiliworld", "bilibili world", "chinajoy", "comicup", "萤火虫",
        "offline", "pop-up", "popup", "exhibition", "concert", "convention", "fan meeting", "anime expo", "comiket", "anime japan",
        "オフライン", "展示会", "原画展", "コラボカフェ", "カフェ", "ポップアップ", "コンサート", "ライブイベント", "コミケ", "オンリー",
        "오프라인", "팝업", "콘서트", "전시회");

    private static readonly string[] Banner = Folded("卡池", "祈愿", "跃迁", "补给", "寻访", "招募", "建造", "唤取", "调频", "颂愿", "扭蛋", "抽卡", "频段",
        "召唤", "概率up", "概率提升", "限定池", "up池",
        "banner", "wish", "warp", "headhunt", "headhunting", "signal search", "convene", "summon", "scout", "gacha", "recruit", "rate up", "rate-up",
        "pick up", "pick-up", "limited-time channel", "exclusive channel", "w-engine channel", "construction",
        "ガチャ", "召喚", "ピックアップ", "スカウト", "建造", "모집", "픽업", "소환", "뽑기");

    private static readonly string[] Version = Folded("版本", "version", "ver.", "更新说明", "更新公告", "update notes", "patch notes", "update announcement", "update details",
        "アップデート", "업데이트");

    private static readonly string[] Collab = Folded("联动", "联名", "合作", "collab", "collaboration", "crossover", "コラボ", "콜라보", "컬래버");

    // Anything that can schedule a player-facing occurrence.
    private static readonly string[] Schedule = Folded("活动", "限时", "期间限定", "复刻", "剧情", "关卡", "签到", "登录", "奖励", "庆典", "周年", "开启", "开催",
        "event", "login", "check-in", "login bonus", "campaign", "anniversary", "rerun", "story", "limited", "festival", "celebration", "duration",
        "イベント", "期间限定", "开催", "キャンペーン", "ログインボーナス", "复刻", "周年",
        "이벤트", "출석", "기간 한정", "기념");

    // Titles that are never calendar items: merchandise launches, wallpapers,
    // fan contests, surveys, rules, guides, compensation and issue reports.
    private static readonly string[] Noise = Folded(
        "商品", "周边", "手办", "模型", "抱枕", "徽章", "吧唧", "立牌", "色纸", "挂件", "挂饰", "亚克力", "钥匙扣",
        "服饰", "睡衣", "衣装", "装扮", "时装", "皮肤", "外观", "旗舰店", "商城", "天猫", "京东", "淘宝", "预售", "预购", "上新",
        "开售", "贩售", "发售", "上架", "现货", "补款", "再贩", "衍生品", "实体卡",
        "merch", "merchandise", "goods", "store", "shop", "pre-order", "preorder", "figure", "plush",
        "グッズ", "フィギュア", "予約", "販売", "通販", "굿즈", "판매",
        "表情包", "贴纸", "头像", "壁纸", "动态壁纸", "桌面", "icon", "emoji", "sticker", "wallpaper", "avatar", "outfit",
        "角色技能演示", "技能演示", "角色演示", "近闻", "剧情pv", "版本pv", "pv音乐", "动画短片", "短片", "ep", "ost", "原声", "音乐专辑", "主题曲",
        "社媒活动", "话题活动", "转发", "抽奖", "投稿", "征集", "获奖", "作品展示", "创作者", "应援", "创作", "绘画",
        "摄影", "cosplay", "同人", "二创", "fan art", "fanart", "screenshot", "photo contest", "giveaway", "creator",
        "生日快乐", "生日贺图", "贺图", "插画", "漫画", "节快乐",
        "问卷", "调研", "调查", "survey", "questionnaire", "アンケート", "설문",
        "问题集中反馈", "集中反馈", "问题反馈", "优化说明", "已知问题", "概率公示", "概率说明", "机率公示", "机率说明", "改善",
        "封禁", "处罚", "违规", "公平运营", "probability details", "known issues", "restricted accounts", "misconduct", "optimization",
        "侧记", "小贴士", "使用指南", "新手指南", "faq", "常见问题", "声明", "防沉迷", "聚合页", "隐私", "用户协议", "招聘",
        "statement", "fair use", "fair play", "privacy", "terms of service",
        "补偿", "compensation", "补发", "お詫び", "不具合", "既知の問題", "보상", "알려진 이슈",
        "trailer", "keeping up with", "showcase", "the latest", "twitch drops", "discord", "dossier", "侦察档案", "draw rates", "rates notice",
        "展示页", "highlights page", "工具更新", "阵容推荐", "走近星穹", "记录统计", "小程序", "签到福利", "米游社", "hoyolab", "pv", "预告片", "fan creation",
        "winners", "phishing", "有奖", "参与讨论", "开奖", "倒计时", "cosmetic", "bundle", "礼包", "特卖", "免费观看", "认证", "special report",
        "专题报道", "幕后", "开发者", "developer", "behind the scenes", "攻略", "guide", "tips", "下载", "预下载", "pre-download", "pre-install", "安装包",
        "家具", "パック", "pack", "立绘", "誓约", "furniture", "skin", "スキン", "着せ替え", "cv:", "cv：", "遊び方", "how to play");

    // 七圣召唤 is a card-game mode, not a summon banner.
    internal static string Prepare(string text) =>
        Text.Fold(text.Normalize(System.Text.NormalizationForm.FormKC)).ToLowerInvariant().Replace("七圣召唤", "七圣");

    internal static bool IsNoise(string title) => Any(Prepare(title), Noise);

    internal static bool IsRelevant(string text)
    {
        var p = Prepare(text);
        return Any(p, Schedule) || Any(p, Banner) || Any(p, Version) || Any(p, Maintenance) || Any(p, Livestream) || Any(p, Offline) || Any(p, Collab);
    }

    internal static bool IsCollab(string text) => Any(Prepare(text), Collab);

    /// <summary>A page or section heading ("News", "公告") picked up as a title.</summary>
    internal static bool IsBareLabel(string title) =>
        Text.IsMatch(Prepare(title).Trim(), @"^(?:news|notices?|announcements?|events?|公告|新闻|资讯|活动|通知|お知らせ|ニュース|イベント|공지|소식|이벤트)$");

    /// <summary>A kind named by the text itself, or null when it names none.</summary>
    internal static EntryKind? KindOf(string text)
    {
        var p = Prepare(text);
        if (Any(p, Maintenance)) return EntryKind.Maintenance;
        if (Any(p, Livestream)) return EntryKind.Livestream;
        if (Any(p, Offline)) return EntryKind.Offline;
        if (Any(p, Banner)) return EntryKind.Banner;
        if (Any(p, Version) || Text.IsMatch(p, @"(?<![a-z])v\d+\.\d+")) return EntryKind.Version;
        return null;
    }

    /// <summary>
    /// A relevant title decides; otherwise the source's own category tag. A body
    /// that merely mentions a banner or maintenance is no evidence of the notice's
    /// kind; only its schedule labels are.
    /// </summary>
    internal static EntryKind Classify(string title, string hint, string body)
    {
        if (KindOf(title) is { } byTitle) return byTitle;
        if (IsRelevant(title)) return EntryKind.Event;
        if (KindOf(hint) is { } byHint) return byHint;
        var p = Prepare(body);
        if (Text.IsMatch(p, @"祈愿时间|跃迁时间|寻访时间|调频时间|唤取时间|招募时间|颂愿时间|卡池时间|召唤时间|ピックアップ期间|召喚期间|(?:wish|warp|banner|signal search|convene)\s+(?:duration|period)"))
            return EntryKind.Banner;
        if (Text.IsMatch(p, @"维护时间|停服时间|メンテナンス(?:日时|时间)|maintenance\s+(?:time|period|duration|schedule)")) return EntryKind.Maintenance;
        return EntryKind.Event;
    }

    /// <summary>True when the title itself names a kind; merging keeps such kinds apart.</summary>
    internal static bool TitleNamesKind(string title) => KindOf(title) is not null;

    /// <summary>Digest notices (version notes, update notes) list several dated items in one body.</summary>
    internal static bool IsDigest(string title) =>
        Text.IsMatch(Prepare(title), @"版本|更新|维护|内容一览|全新内容|update|patch|maintenance|アップデート|メンテナンス|업데이트|점검|新内容|版本内容|活动一览|活动预告|イベント情报|events?\s+(?:schedule|overview|preview)");

    /// <summary>"4.6版本期间", "during Version 3.3": the schedule is the version (display) window.</summary>
    internal static bool HasScheduleLabel(string body) =>
        Text.IsMatch(Prepare(body), @"活动时间|活动期间|开放时间|开放期间|开启时间|开催期间|实施期间|event\s+(?:period|duration)|이벤트\s*기간") || SpansVersion(body);

    internal static bool SpansVersion(string body) =>
        Text.IsMatch(Prepare(body), @"版本(?:持续)?期间|版本内|常驻|永久开放|during\s+(?:the\s+)?(?:current\s+)?version|version\s+period|バージョン期间|버전\s*기간");

    private static bool Any(string prepared, string[] words)
    {
        foreach (var word in words)
            if (Contains(prepared, word)) return true;
        return false;
    }

    private static bool Contains(string text, string word)
    {
        if (word.All(c => c < 128))
            return word.Length <= 2
                ? Text.IsMatch(text, @"(?<![a-z])" + Regex.Escape(word) + @"(?![a-z])")
                : Text.IsMatch(text, @"\b" + Regex.Escape(word) + @"(?:s|es|d|ed|ing)?\b");
        return text.Contains(word, StringComparison.Ordinal);
    }

    private static string[] Folded(params string[] words) => words.Select(w => Text.Fold(w.Normalize(System.Text.NormalizationForm.FormKC)).ToLowerInvariant()).ToArray();
}
