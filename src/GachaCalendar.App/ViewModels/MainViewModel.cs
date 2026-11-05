using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GachaCalendar.Core;

namespace GachaCalendar.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppStore store;
    private readonly HttpClient http = SourceFetcher.CreateClient();
    private readonly SourceFetcher fetcher;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMinutes(10) };
    private Settings settings;
    private List<Entry> entries;
    private CancellationTokenSource? refreshing;

    public MainViewModel(AppStore store)
    {
        this.store = store;
        fetcher = new SourceFetcher(http, store);
        settings = store.LoadSettings();
        entries = store.LoadSnapshot();
        foreach (var game in settings.Games) Games.Add(Track(new GameItem(game)));
        Kinds =
        [
            .. new[] { EntryKind.Version, EntryKind.Banner, EntryKind.Event, EntryKind.Offline, EntryKind.Livestream, EntryKind.Maintenance }
                .Select(k => Watch(new KindFilter(k, Labels.Kind(k), !settings.HiddenKinds.Contains(k)))),
        ];
        Collab = Watch(new KindFilter(null, "仅联动", false));
        isTimeline = settings.View != "list";
        sort = settings.Sort;
        showDone = settings.ShowDone;
        dayWidth = settings.DayWidth;
        Reports = settings.Reports;
        Rebuild();
        UpdateStatus();
        timer.Tick += (_, _) => { if (settings.RefreshHours > 0 && settings.LastRefresh < DateTime.Now.AddHours(-settings.RefreshHours)) _ = Refresh(); };
        timer.Start();
        if (settings.LastRefresh is null || settings.LastRefresh < DateTime.Now.AddMinutes(-30) || entries.Count == 0)
            Dispatcher.UIThread.Post(() => _ = Refresh(), DispatcherPriority.Background);
    }

    public ObservableCollection<GameItem> Games { get; } = [];
    public IReadOnlyList<KindFilter> Kinds { get; }
    public KindFilter Collab { get; }
    public List<SourceReport> Reports { get; private set; }
    public string Theme => settings.Theme;
    public Settings Settings => settings;

    /// <summary>Null shows every visible game.</summary>
    [ObservableProperty] private GameItem? focus;
    [ObservableProperty] private string search = "";
    [ObservableProperty] private bool onlyStarred;
    [ObservableProperty] private bool showDone;
    [ObservableProperty] private bool isTimeline;
    [ObservableProperty] private string sort;
    [ObservableProperty] private double dayWidth;
    [ObservableProperty] private bool busy;
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string progress = "";
    [ObservableProperty] private int failed;
    [ObservableProperty] private int hiddenCount;
    [ObservableProperty] private EntryItem? selected;
    [ObservableProperty] private IReadOnlyList<EntryItem> visible = [];
    [ObservableProperty] private IReadOnlyList<TimelineRow> rows = [];
    [ObservableProperty] private string summary = "";

    public bool IsList { get => !IsTimeline; set => IsTimeline = !value; }
    public bool HasHidden => HiddenCount > 0;
    private static readonly string[] Sorts = ["ending", "start", "game"];
    public int SortIndex { get => Math.Max(0, Array.IndexOf(Sorts, Sort)); set => Sort = Sorts[Math.Clamp(value, 0, Sorts.Length - 1)]; }
    public bool HasFailed => Failed > 0;
    public bool HasSelection => Selected is not null;
    public string FocusTitle => Focus is null ? "全部游戏" : Focus.Config.DisplayName;

    partial void OnFocusChanged(GameItem? value) { OnPropertyChanged(nameof(FocusTitle)); Rebuild(); }
    partial void OnSearchChanged(string value) => Rebuild();
    partial void OnOnlyStarredChanged(bool value) => Rebuild();
    partial void OnShowDoneChanged(bool value) { settings = settings with { ShowDone = value }; Rebuild(); }
    partial void OnIsTimelineChanged(bool value) { OnPropertyChanged(nameof(IsList)); settings = settings with { View = value ? "timeline" : "list" }; }
    partial void OnSortChanged(string value) { settings = settings with { Sort = value }; Rebuild(); }
    partial void OnDayWidthChanged(double value) => settings = settings with { DayWidth = value };
    partial void OnSelectedChanged(EntryItem? value) => OnPropertyChanged(nameof(HasSelection));
    partial void OnHiddenCountChanged(int value) => OnPropertyChanged(nameof(HasHidden));
    partial void OnFailedChanged(int value) => OnPropertyChanged(nameof(HasFailed));

    private GameItem Track(GameItem item)
    {
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(GameItem.Visible)) return;
            SaveGames();
            Rebuild();
        };
        return item;
    }

    private KindFilter Watch(KindFilter filter)
    {
        filter.PropertyChanged += (_, _) =>
        {
            settings = settings with { HiddenKinds = Kinds?.Where(k => !k.On && k.Kind is not null).Select(k => k.Kind!.Value).ToList() ?? settings.HiddenKinds };
            Rebuild();
        };
        return filter;
    }

    // ---- filtering and layout ----

    private void Rebuild()
    {
        if (Kinds is null) return;
        var games = Games.ToDictionary(g => g.Id);
        var kinds = Kinds.Where(k => k.On).Select(k => k.Kind!.Value).ToHashSet();
        var query = Search.Trim();
        var shown = new List<EntryItem>();
        var hidden = 0;
        foreach (var entry in entries)
        {
            if (!games.TryGetValue(entry.GameId, out var game)) continue;
            if (Focus is null ? !game.Visible : Focus.Id != game.Id) continue;
            var mark = settings.Marks.GetValueOrDefault(entry.Key) ?? new EntryMark();
            if (mark.Hidden) { hidden++; continue; }
            if (!kinds.Contains(entry.Kind) || Collab.On && !entry.IsCollab) continue;
            if (OnlyStarred && !mark.Starred || !ShowDone && mark.Done) continue;
            if (query.Length > 0 && !entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !game.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            shown.Add(new EntryItem(entry, game, mark));
        }
        HiddenCount = hidden;
        var today = DateTime.Today;
        Visible = (Sort switch
        {
            "start" => shown.OrderByDescending(e => e.Entry.Start).ThenBy(e => e.Entry.End),
            "game" => shown.OrderBy(e => Games.IndexOf(e.Game)).ThenBy(e => e.Entry.Start),
            // Running entries first by how soon they end, then upcoming, then finished.
            _ => shown.OrderBy(e => e.Entry.End < today ? 2 : e.Entry.Start > today ? 1 : 0)
                .ThenBy(e => e.Entry.End < today ? -e.Entry.End.Ticks : e.Entry.Start > today ? e.Entry.Start.Ticks : e.Entry.End.Ticks)
        }).ThenByDescending(e => e.Starred).ToList();
        Rows = Layout(shown);
        if (Selected is { } current) Selected = Visible.FirstOrDefault(e => e.Entry.Key == current.Entry.Key);
        foreach (var game in Games) game.Count = entries.Count(e => e.GameId == game.Id && e.End >= today);
        var running = shown.Count(e => e.Entry.Start <= today && e.Entry.End >= today);
        var soon = shown.Count(e => e.EndingSoon);
        Summary = $"{shown.Count} 条 · 进行中 {running}" + (soon > 0 ? $" · {soon} 条 3 天内结束" : "");
    }

    /// <summary>Timeline rows per game: entries packed into lanes so bars never overlap.</summary>
    private List<TimelineRow> Layout(List<EntryItem> shown)
    {
        var rows = new List<TimelineRow>();
        foreach (var game in Games)
        {
            var items = shown.Where(e => e.Game == game).OrderByDescending(e => e.Starred)
                .ThenBy(e => e.Entry.Kind is EntryKind.Version ? 0 : 1).ThenBy(e => e.Entry.Start).ThenByDescending(e => e.Entry.Days).ToList();
            if (items.Count == 0) continue;
            var laneEnds = new List<DateTime>();
            var bars = new List<TimelineBar>();
            foreach (var item in items)
            {
                var lane = laneEnds.FindIndex(end => end < item.Entry.Start);
                if (lane < 0) { lane = laneEnds.Count; laneEnds.Add(item.Entry.End); }
                else laneEnds[lane] = item.Entry.End;
                bars.Add(new TimelineBar(item, lane));
            }
            rows.Add(new TimelineRow(game, bars, laneEnds.Count));
        }
        return rows;
    }

    // ---- refresh ----

    [RelayCommand]
    private async Task Refresh()
    {
        refreshing?.Cancel();
        var cts = refreshing = new CancellationTokenSource();
        Busy = true;
        var games = Games.Select(g => g.Config).ToList();
        var total = games.Sum(g => g.Sources.Count);
        var done = 0;
        Progress = $"0/{total}";
        try
        {
            var progress = new Progress<SourceReport>(_ => Progress = $"{++done}/{total}");
            var (fresh, reports) = await Task.Run(() => fetcher.FetchAllAsync(games, new FetchOptions { RetentionMonths = settings.RetentionMonths }, progress, cts.Token));
            if (cts.IsCancellationRequested) return;
            entries = fresh;
            Reports = reports;
            settings = settings with { LastRefresh = DateTime.Now, Reports = reports };
            store.SaveSnapshot(entries);
            Save();
            OnPropertyChanged(nameof(Reports));
            Rebuild();
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (refreshing == cts) { Busy = false; Progress = ""; }
            UpdateStatus();
        }
    }

    private void UpdateStatus()
    {
        var bad = Reports.Where(r => r.State is SourceState.Failed or SourceState.Cached).ToList();
        Failed = bad.Count;
        foreach (var game in Games)
        {
            var mine = Reports.Where(r => r.GameId == game.Id).ToList();
            game.Failed = mine.Count > 0 && mine.All(r => r.State is SourceState.Failed or SourceState.Cached or SourceState.Empty);
            game.Tip = string.Join("\n", mine.Select(r => $"{r.Source}：{r.Message}"));
        }
        Status = settings.LastRefresh is { } at ? $"更新于 {at:MM-dd HH:mm}" : "尚未刷新";
    }

    // ---- marks ----

    [RelayCommand]
    private void ToggleStar(EntryItem? item) => Mark(item, m => m with { Starred = !m.Starred });

    [RelayCommand]
    private void ToggleDone(EntryItem? item) => Mark(item, m => m with { Done = !m.Done });

    [RelayCommand]
    private void Hide(EntryItem? item)
    {
        Mark(item, m => m with { Hidden = true });
        Selected = null;
    }

    [RelayCommand]
    private void RestoreHidden()
    {
        settings = settings with { Marks = settings.Marks.Where(m => !m.Value.Hidden).ToDictionary() };
        Save();
        Rebuild();
    }

    private void Mark(EntryItem? item, Func<EntryMark, EntryMark> change)
    {
        if (item is null) return;
        var key = item.Entry.Key;
        var mark = change(settings.Marks.GetValueOrDefault(key) ?? new EntryMark()) with { Updated = DateTime.Now };
        var marks = new Dictionary<string, EntryMark>(settings.Marks);
        if (mark.IsEmpty) marks.Remove(key); else marks[key] = mark;
        settings = settings with { Marks = marks };
        item.Starred = mark.Starred;
        item.Done = mark.Done;
        Save();
        Rebuild();
    }

    // ---- games ----

    [RelayCommand]
    private void ShowAll() => Focus = null;

    public void AddGames(IEnumerable<GameConfig> configs)
    {
        foreach (var config in configs)
        {
            var id = config.Id;
            for (var i = 2; Games.Any(g => g.Id == id); i++) id = $"{config.Id}-{i}";
            Games.Add(Track(new GameItem(config with { Id = id })));
        }
        SaveGames();
        _ = Refresh();
    }

    public void RemoveGame(GameItem game)
    {
        Games.Remove(game);
        if (Focus == game) Focus = null;
        entries = entries.Where(e => e.GameId != game.Id).ToList();
        SaveGames();
        Rebuild();
    }

    public void UpdateGame(GameItem game, GameConfig config)
    {
        game.Update(config);
        SaveGames();
        _ = Refresh();
    }

    public void MoveGame(GameItem game, int offset)
    {
        var index = Games.IndexOf(game);
        var target = Math.Clamp(index + offset, 0, Games.Count - 1);
        if (index < 0 || target == index) return;
        Games.Move(index, target);
        SaveGames();
        Rebuild();
    }

    public void ApplySettings(int retention, int refreshHours, string theme)
    {
        settings = settings with { RetentionMonths = retention, RefreshHours = refreshHours, Theme = theme };
        App.ApplyTheme(theme);
        Save();
    }

    public string ExportIcs() => IcsWriter.Write(Visible.Select(e => e.Entry), Games.ToDictionary(g => g.Id, g => g.Config));

    private void SaveGames()
    {
        settings = settings with { Games = Games.Select(g => g.Config with { Visible = g.Visible }).ToList() };
        Save();
    }

    public void Save() => store.SaveSettings(settings);

    public string DataFolder => store.Folder;
}
