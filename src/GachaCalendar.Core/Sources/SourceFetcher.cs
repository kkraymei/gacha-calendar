using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GachaCalendar.Core;

public sealed record FetchOptions
{
    public DateTime? Today { get; init; }
    /// <summary>Entries that started earlier than this and already ended are dropped.</summary>
    public int RetentionMonths { get; init; } = 3;
    /// <summary>Generic link following for list pages without a detail template.</summary>
    public int FollowLimit { get; init; } = 15;
}

public sealed record SourceResult(List<Entry> Entries, SourceReport Report);

public interface ISourceCache
{
    CachedSource? Load(string key);
    void Save(string key, CachedSource value);
}

public sealed record CachedSource(DateTime At, List<Entry> Entries);

/// <summary>
/// Fetches one configured source: the list request, an optional joined response,
/// per-row detail requests or same-site link following, then builds entries.
/// Failures fall back to the last successful result.
/// </summary>
public sealed class SourceFetcher(HttpClient http, ISourceCache? cache = null, int concurrency = 6)
{
    private const int MaxBytes = 16_000_000;
    private readonly SemaphoreSlim gate = new(concurrency);

    // GBK/Big5/Shift-JIS pages declare their charset; .NET needs the code-page provider for them.
    static SourceFetcher() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static HttpClient CreateClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(20)
        }) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/html, application/rss+xml, application/atom+xml, text/calendar;q=0.9, */*;q=0.5");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.8,ja;q=0.7");
        return client;
    }

    public static string CacheKey(GameConfig game, SourceSpec source) => $"{game.Id}|{source.Method}|{source.Url}|{source.Body}";

    /// <summary>Fetches every source of the games, merging copies of one notice per game.</summary>
    public async Task<(List<Entry> Entries, List<SourceReport> Reports)> FetchAllAsync(IEnumerable<GameConfig> games, FetchOptions options,
        IProgress<SourceReport>? progress = null, CancellationToken ct = default)
    {
        var list = games.ToList();
        var tasks = list.SelectMany(g => g.Sources.Select(async s =>
        {
            var result = await FetchAsync(g, s, options, ct).ConfigureAwait(false);
            progress?.Report(result.Report);
            return result;
        })).ToList();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var names = list.GroupBy(g => g.Id).ToDictionary(g => g.Key, g => g.First().Name);
        return (Merger.Merge(results.SelectMany(r => r.Entries), names), results.Select(r => r.Report).ToList());
    }

    public async Task<SourceResult> FetchAsync(GameConfig game, SourceSpec source, FetchOptions options, CancellationToken ct = default)
    {
        var name = string.IsNullOrWhiteSpace(source.Name) ? SourceName(source.Url) : source.Name!;
        var key = CacheKey(game, source);
        var today = (options.Today ?? DateTime.Today).Date;
        try
        {
            var warnings = new List<string>();
            var notices = await NoticesAsync(source, options, warnings, ct).ConfigureAwait(false);
            var entries = Entries(notices, game, source, name, today, options.RetentionMonths);
            cache?.Save(key, new CachedSource(DateTime.Now, entries));
            var state = warnings.Count > 0 ? SourceState.Partial : entries.Count == 0 ? SourceState.Empty : SourceState.Ok;
            var message = entries.Count == 0
                ? "没有识别到保留范围内的活动。页面可能依赖脚本渲染、日期写在图片中，或近期没有公告。"
                : $"识别 {entries.Count} 条。";
            if (warnings.Count > 0) message += $" {warnings.Count} 个请求失败：" + string.Join("；", warnings.Take(3));
            return new(entries, new(game.Id, name, source.Url, state, entries.Count, message, DateTime.Now));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var cached = cache?.Load(key);
            var kept = cached?.Entries.Where(e => InWindow(e, today, options.RetentionMonths)).ToList() ?? [];
            var message = Describe(ex) + (kept.Count > 0 ? $"；使用 {cached!.At:MM-dd HH:mm} 的缓存 {kept.Count} 条。" : "");
            return new(kept, new(game.Id, name, source.Url, kept.Count > 0 ? SourceState.Cached : SourceState.Failed, kept.Count, message, DateTime.Now));
        }
    }

    private static List<Entry> Entries(IEnumerable<Notice> notices, GameConfig game, SourceSpec source, string name, DateTime today, int retention) =>
        Merger.Merge(notices.SelectMany(n => EntryBuilder.Build(n, game.Id, name, Page(source) ?? source.Url, today))
            .Where(e => InWindow(e, today, retention)), new Dictionary<string, string> { [game.Id] = game.Name });

    /// <summary>Entries from one response, without further requests (offline parsing and tests).</summary>
    internal static List<Entry> Parse(string text, string contentType, SourceSpec source, GameConfig game, DateTime today, int retention = 3)
    {
        var notices = NoticeReader.Read(text, contentType, source.Url, source.StartIsPublished, today).Select(n => n with { Url = PageUrl(source, n) });
        return Entries(notices, game, source, source.Name ?? SourceName(source.Url), today, retention);
    }

    /// <summary>Notices of one source after joining and detail expansion, before classification.</summary>
    internal async Task<List<Notice>> NoticesAsync(SourceSpec source, FetchOptions options, List<string> warnings, CancellationToken ct = default)
    {
        var today = (options.Today ?? DateTime.Today).Date;
        var (text, type) = await SendAsync(new RequestSpec { Url = source.Url, Method = source.Method, Headers = source.Headers, Body = source.Body }, ct).ConfigureAwait(false);
        if (source.Join is { } join)
        {
            try { text = JoinRows(text, (await SendAsync(join, ct).ConfigureAwait(false)).Text, join.Key ?? "id"); }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { warnings.Add("正文：" + Describe(ex)); }
        }
        var notices = NoticeReader.Read(text, type, source.Url, source.StartIsPublished, today);
        notices = source.Detail is { } detail
            ? await ExpandAsync(notices, source, detail, options, warnings, ct).ConfigureAwait(false)
            : await FollowAsync(notices, text, source, options, warnings, ct).ConfigureAwait(false);
        return notices.Select(n => n with { Url = PageUrl(source, n) }).ToList();
    }

    /// <summary>Running and upcoming entries, recent past ones; unconfirmed schedules only while they are news.</summary>
    internal static bool InWindow(Entry entry, DateTime today, int retentionMonths) =>
        entry.End >= today || entry.Start >= (entry.Uncertain ? today.AddDays(-30) : today.AddMonths(-Math.Max(1, retentionMonths)));

    // ---- detail templates ----

    private async Task<List<Notice>> ExpandAsync(List<Notice> notices, SourceSpec source, RequestSpec detail, FetchOptions options,
        List<string> warnings, CancellationToken ct)
    {
        var today = (options.Today ?? DateTime.Today).Date;
        var chosen = notices
            .Select((notice, index) => (notice, index, request: notice.Fields is null ? null : Fill(detail, notice.Fields)))
            .Where(x => x.request is not null && Wanted(Text.Clean(x.notice.Title)) && (x.notice.Published is null || x.notice.Published >= today.AddMonths(-Math.Max(1, options.RetentionMonths))))
            .OrderByDescending(x => x.notice.Published ?? DateTime.MinValue).Take(Math.Max(1, detail.Limit)).ToList();
        var replaced = new Notice?[notices.Count];
        await Task.WhenAll(chosen.Select(async x =>
        {
            try
            {
                var (text, type) = await SendAsync(x.request!, ct).ConfigureAwait(false);
                replaced[x.index] = Combine(x.notice, NoticeReader.Read(text, type, x.request!.Url, source.StartIsPublished, today), text, x.request.Url);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                lock (warnings) warnings.Add(Describe(ex));
            }
        })).ConfigureAwait(false);
        return notices.Select((n, i) => replaced[i] ?? n).ToList();
    }

    /// <summary>List pages without a template: follow same-site links of notices that lack a schedule.</summary>
    private async Task<List<Notice>> FollowAsync(List<Notice> notices, string text, SourceSpec source, FetchOptions options,
        List<string> warnings, CancellationToken ct)
    {
        var today = (options.Today ?? DateTime.Today).Date;
        var targets = notices.Select((n, i) => (n, i))
            .Where(x => x.n.Url is { } url && url != source.Url && Html.SameSite(url, source.Url) && Wanted(Text.Clean(x.n.Title))
                && x.n.Start is null && !Dates.HasRange(Text.Clean(x.n.Body)))
            .OrderByDescending(x => x.n.Published ?? DateTime.MinValue).Take(options.FollowLimit).ToList();
        var replaced = new Notice?[notices.Count];
        await Task.WhenAll(targets.Select(async x =>
        {
            try
            {
                var (page, type) = await SendAsync(new RequestSpec { Url = x.n.Url! }, ct).ConfigureAwait(false);
                replaced[x.i] = Combine(x.n, NoticeReader.Read(page, type, x.n.Url!, source.StartIsPublished, today), page, x.n.Url!);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                lock (warnings) warnings.Add(Describe(ex));
            }
        })).ConfigureAwait(false);
        var result = notices.Select((n, i) => replaced[i] ?? n).ToList();
        // A feed or calendar the page links to is read as an extra notice source.
        if (notices.Count == 0 && !text.TrimStart().StartsWith('{'))
            foreach (var link in Html.Links(text, source.Url).Where(l => Text.IsMatch(l, @"(?:feed|rss|atom|\.ics)(?:$|[/?])")).Take(2))
            {
                try
                {
                    var (feed, type) = await SendAsync(new RequestSpec { Url = link }, ct).ConfigureAwait(false);
                    result.AddRange(NoticeReader.Read(feed, type, link, source.StartIsPublished, today));
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { warnings.Add(Describe(ex)); }
            }
        return result;
    }

    private static bool Wanted(string title) => Rules.IsRelevant(title) && !Rules.IsNoise(title);

    /// <summary>The detail response's notice, keeping the list's title, date and placeholders where the detail is silent.</summary>
    private static Notice Combine(Notice row, List<Notice> found, string raw, string url)
    {
        var best = found.OrderByDescending(n => Similar(n.Title, row.Title)).ThenByDescending(n => n.Body.Length).FirstOrDefault();
        if (best is null)
        {
            var single = Html.Single(raw, url);
            if (single is null) return row;
            best = new Notice { Title = single.Title, Body = single.Body, Published = Dates.Parse(single.Published) };
        }
        var rowTitle = Text.Clean(row.Title);
        var truncated = rowTitle.EndsWith("...") || rowTitle.EndsWith('…') || rowTitle.Length < 4;
        return row with
        {
            Title = truncated && best.Title.Length > 0 ? best.Title : row.Title,
            Body = best.Body.Length > row.Body.Length ? best.Body : row.Body,
            Published = row.Published ?? best.Published,
            Start = row.Start ?? best.Start,
            End = row.End ?? best.End,
            FallbackEnd = row.FallbackEnd ?? best.FallbackEnd,
            Hint = (row.Hint + " " + best.Hint).Trim()
        };
    }

    private static int Similar(string a, string b)
    {
        a = Rules.Prepare(Text.Clean(a)); b = Rules.Prepare(Text.Clean(b));
        if (a.Length == 0 || b.Length == 0) return 0;
        var prefix = 0;
        while (prefix < Math.Min(a.Length, b.Length) && a[prefix] == b[prefix]) prefix++;
        return prefix + (a.Contains(b) || b.Contains(a) ? 50 : 0);
    }

    // ---- placeholders and pages ----

    private static readonly Regex Placeholder = new(@"\{([A-Za-z_][\w]*)\}", RegexOptions.Compiled);

    internal static RequestSpec? Fill(RequestSpec template, IReadOnlyDictionary<string, string> fields)
    {
        var url = FillText(template.Url, fields, FillMode.Url);
        if (url is null) return null;
        var body = template.Body is null ? null : FillText(template.Body, fields, FillMode.Json);
        return template.Body is not null && body is null ? null : template with { Url = url, Body = body };
    }

    private enum FillMode { Url, Json, Raw }

    private static string? FillText(string template, IReadOnlyDictionary<string, string> fields, FillMode mode)
    {
        var missing = false;
        var whole = Placeholder.Match(template) is { Success: true } m && m.Length == template.Length;
        var result = Placeholder.Replace(template, match =>
        {
            if (!fields.TryGetValue(match.Groups[1].Value, out var value) || value.Length == 0) { missing = true; return ""; }
            return whole || mode == FillMode.Raw ? value
                : mode == FillMode.Json ? JsonEncodedText.Encode(value).ToString()
                : Uri.EscapeDataString(value);
        });
        return missing ? null : result;
    }

    private static string? Page(SourceSpec source) =>
        source.Page is { } page && !Placeholder.IsMatch(page) ? page : null;

    /// <summary>A link a person can open: the page template, else the notice's own link, else the source's page.</summary>
    private static string? PageUrl(SourceSpec source, Notice notice)
    {
        if (source.Page is { } page && Placeholder.IsMatch(page) && notice.Fields is { } fields && FillText(page, fields, FillMode.Url) is { } filled)
            return Text.Url(filled, source.Url) ?? notice.Url;
        if (notice.Url is { } own && !LooksLikeApi(own)) return own;
        return Page(source) ?? notice.Url;
    }

    private static bool LooksLikeApi(string url) => Text.IsMatch(url, @"/api/|\.json(?:$|\?)|\.action(?:$|\?)|getAnn|getContent|InformationFeedsSvr");

    public static string SourceName(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? (uri.Host.StartsWith("www.") ? uri.Host[4..] : uri.Host) : "自定义来源";

    // ---- HTTP ----

    private async Task<(string Text, string Type)> SendAsync(RequestSpec request, CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                try { return await SendOnceAsync(request, ct).ConfigureAwait(false); }
                catch (Exception ex) when (attempt == 0 && IsTransient(ex, ct)) { await Task.Delay(800, ct).ConfigureAwait(false); }
            }
        }
        finally { gate.Release(); }
    }

    private static bool IsTransient(Exception ex, CancellationToken ct) =>
        ex is TaskCanceledException && !ct.IsCancellationRequested
        || ex is HttpRequestException { StatusCode: null or >= HttpStatusCode.InternalServerError };

    private async Task<(string Text, string Type)> SendOnceAsync(RequestSpec request, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(new HttpMethod(request.Method.ToUpperInvariant()), request.Url);
        if (request.Body is { } body)
            message.Content = new StringContent(body, Encoding.UTF8, body.TrimStart().StartsWith('{') ? "application/json" : "application/x-www-form-urlencoded");
        foreach (var (key, value) in request.Headers ?? [])
            if (!message.Headers.TryAddWithoutValidation(key, value)) message.Content?.Headers.TryAddWithoutValidation(key, value);
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)response.StatusCode}", null, response.StatusCode);
        if (response.Content.Headers.ContentLength > MaxBytes) throw new HttpRequestException("响应过大");
        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        if (bytes.Length > MaxBytes) throw new HttpRequestException("响应过大");
        var type = response.Content.Headers.ContentType?.MediaType ?? "";
        var charset = response.Content.Headers.ContentType?.CharSet;
        var encoding = Encoding.UTF8;
        try { if (!string.IsNullOrWhiteSpace(charset)) encoding = Encoding.GetEncoding(charset.Trim('"')); } catch (ArgumentException) { }
        var text = encoding.GetString(bytes);
        CheckApiError(text);
        return (text, type);
    }

    /// <summary>JSON APIs answer errors with HTTP 200 and a code; surface them instead of "no entries".</summary>
    private static void CheckApiError(string text)
    {
        var trimmed = text.TrimStart('﻿', ' ', '\r', '\n', '\t');
        if (!trimmed.StartsWith('{')) return;
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            string Message() => root.TryGetProperty("message", out var m) || root.TryGetProperty("msg", out m) ? m.ToString() : "";
            if (root.TryGetProperty("retcode", out var retcode) && retcode.ValueKind == JsonValueKind.Number && retcode.GetInt64() != 0)
                throw new HttpRequestException($"接口返回错误 {retcode.GetInt64()} {Message()}".Trim());
            if (root.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number && code.GetInt64() != 0
                && (!root.TryGetProperty("data", out var data) || data.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined))
                throw new HttpRequestException($"接口返回错误 {code.GetInt64()} {Message()}".Trim());
        }
        catch (JsonException) { }
    }

    /// <summary>Copies fields of the second response into list rows sharing the key (e.g. notice bodies by ann_id).</summary>
    internal static string JoinRows(string listJson, string joinJson, string key)
    {
        var extra = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var row in Objects(JsonNode.Parse(joinJson)))
            if (row[key] is { } id) extra.TryAdd(id.ToString(), row);
        var root = JsonNode.Parse(listJson);
        foreach (var row in Objects(root).ToList())
        {
            if (row[key] is not { } id || !extra.TryGetValue(id.ToString(), out var source) || ReferenceEquals(source, row)) continue;
            foreach (var (name, value) in source.ToList())
            {
                if (value is null || value is JsonObject or JsonArray) continue;
                var current = row[name]?.ToString();
                var incoming = value.ToString();
                if (string.IsNullOrEmpty(current) || incoming.Length > current.Length) row[name] = value.DeepClone();
            }
        }
        return root?.ToJsonString() ?? listJson;
    }

    private static IEnumerable<JsonObject> Objects(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            yield return obj;
            foreach (var (_, value) in obj.ToList())
                foreach (var child in Objects(value)) yield return child;
        }
        else if (node is JsonArray array)
            foreach (var item in array.ToList())
                foreach (var child in Objects(item)) yield return child;
    }

    private static string Describe(Exception ex) => ex switch
    {
        TaskCanceledException => "请求超时",
        HttpRequestException { StatusCode: { } status } => $"HTTP {(int)status}",
        HttpRequestException h when h.InnerException is { } inner => inner.Message,
        _ => ex.Message
    };
}
