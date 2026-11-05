using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using GachaCalendar.Core;

namespace GachaCalendar.App.ViewModels;

/// <summary>Fixed kind colours, legible on light and dark backgrounds.</summary>
public static class Palette
{
    public static Color Kind(EntryKind kind) => kind switch
    {
        EntryKind.Version => Color.Parse("#8B6CF0"),
        EntryKind.Banner => Color.Parse("#E8577D"),
        EntryKind.Event => Color.Parse("#3B82F6"),
        EntryKind.Offline => Color.Parse("#F59E0B"),
        EntryKind.Livestream => Color.Parse("#14B8A6"),
        _ => Color.Parse("#8C96AA")
    };

    public static readonly Color Collab = Color.Parse("#F97316");
    public static readonly Color Star = Color.Parse("#F5B301");

    public static IBrush KindBrush(EntryKind kind) => new SolidColorBrush(Kind(kind));

    public static Color Parse(string hex) => Color.TryParse(hex, out var color) ? color : Color.Parse("#6B7A90");
}

public sealed partial class GameItem(GameConfig config) : ObservableObject
{
    public GameConfig Config { get; private set; } = config;
    public string Id => Config.Id;
    public string Name => Config.Name;
    public string Region => Config.Region;
    public IBrush Brush => new SolidColorBrush(Palette.Parse(Config.Color));
    public Color Color => Palette.Parse(Config.Color);

    [ObservableProperty] private bool visible = config.Visible;
    [ObservableProperty] private int count;
    [ObservableProperty] private bool failed;
    [ObservableProperty] private string tip = "";

    public override string ToString() => Config.DisplayName;

    public void Update(GameConfig updated)
    {
        Config = updated;
        OnPropertyChanged(nameof(Config));
    }
}

public sealed partial class KindFilter(EntryKind? kind, string label, bool on) : ObservableObject
{
    /// <summary>Null stands for the collaboration flag.</summary>
    public EntryKind? Kind { get; } = kind;
    public string Label { get; } = label;
    public IBrush Brush { get; } = new SolidColorBrush(kind is { } k ? Palette.Kind(k) : Palette.Collab);
    [ObservableProperty] private bool on = on;
}

/// <summary>An entry with the user's marks and display text.</summary>
public sealed partial class EntryItem : ObservableObject
{
    public EntryItem(Entry entry, GameItem game, EntryMark mark)
    {
        Entry = entry;
        Game = game;
        starred = mark.Starred;
        done = mark.Done;
    }

    public Entry Entry { get; }
    public GameItem Game { get; }

    [ObservableProperty] private bool starred;
    [ObservableProperty] private bool done;

    public string Title => Entry.Title;
    public string Kind => Labels.Kind(Entry.Kind);
    public IBrush KindBrush => Palette.KindBrush(Entry.Kind);
    public Color KindColor => Palette.Kind(Entry.Kind);
    public bool IsCollab => Entry.IsCollab;
    public bool Uncertain => Entry.Uncertain;
    public string GameName => Game.Name;
    public string Region => Game.Region;
    public string Summary => Entry.Summary;
    public string? Parent => Entry.Parent is { } parent ? "摘自：" + parent : null;
    public IReadOnlyList<Origin> Origins => Entry.Origins;
    public bool DateConflict => Entry.DateConflict;

    public string Dates => Entry.Start == Entry.End
        ? $"{Entry.Start:yyyy/MM/dd}" + (Entry.Uncertain ? "（发布日期，活动时间待确认）" : "")
        : $"{Entry.Start:yyyy/MM/dd} – {Entry.End:yyyy/MM/dd} · {Entry.Days} 天" + (Entry.Uncertain ? "（待确认）" : "");

    public string ShortDates => Entry.Start == Entry.End ? $"{Entry.Start:MM/dd}" : $"{Entry.Start:MM/dd} – {Entry.End:MM/dd}";

    public string Remaining => Describe(DateTime.Today);

    /// <summary>Share of the run that has passed, for progress bars.</summary>
    public double Progress
    {
        get
        {
            var today = DateTime.Today;
            if (today < Entry.Start) return 0;
            if (today > Entry.End) return 1;
            return (today - Entry.Start).TotalDays / Math.Max(1, Entry.Days);
        }
    }

    public bool EndingSoon => !Entry.Uncertain && Entry.End >= DateTime.Today && (Entry.End - DateTime.Today).TotalDays <= 3 && Entry.Start <= DateTime.Today;

    public string Describe(DateTime today)
    {
        if (Entry.Uncertain && Entry.Start == Entry.End) return Entry.Start <= today ? "时间待确认" : $"{(Entry.Start - today).Days} 天后";
        if (today < Entry.Start) return (Entry.Start - today).Days == 1 ? "明天开始" : $"{(Entry.Start - today).Days} 天后开始";
        if (today > Entry.End) return "已结束";
        var left = (Entry.End - today).Days;
        return left == 0 ? "今天结束" : $"剩 {left} 天";
    }

    public override string ToString() => $"{Title} · {GameName} · {ShortDates}";

    public void Refresh()
    {
        OnPropertyChanged(nameof(Remaining));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(EndingSoon));
    }
}

public sealed record TimelineBar(EntryItem Item, int Lane);

public sealed record TimelineRow(GameItem Game, IReadOnlyList<TimelineBar> Bars, int Lanes);
