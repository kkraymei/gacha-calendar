using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using GachaCalendar.App.ViewModels;
using GachaCalendar.Core;

namespace GachaCalendar.App.Views;

/// <summary>Small helpers shared by the dialogs, which are built in code.</summary>
internal static class Dialogs
{
    internal static Window Frame(string title, double width, double height) => new()
    {
        Title = title, Width = width, Height = height, MinWidth = width * 0.8, MinHeight = height * 0.7,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, CanResize = true
    };

    internal static TextBlock Text(string text, string? classes = null, bool wrap = true)
    {
        var block = new TextBlock { Text = text, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap };
        if (classes is not null) block.Classes.Add(classes);
        return block;
    }

    internal static Button Button(string text, bool primary = false)
    {
        var button = new Button { Content = text, MinWidth = 84, HorizontalContentAlignment = HorizontalAlignment.Center };
        button.Classes.Add(primary ? "primary" : "flat");
        return button;
    }

    internal static StackPanel Actions(params Control[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        panel.Children.AddRange(buttons);
        return panel;
    }

    internal static async Task<bool> Confirm(Window owner, string title, string message)
    {
        var window = Frame("确认", 420, 200);
        var ok = Button("确定", primary: true);
        var cancel = Button("取消");
        ok.Click += (_, _) => window.Close(true);
        cancel.Click += (_, _) => window.Close(false);
        window.Content = new DockPanel
        {
            Margin = new Thickness(20),
            Children = { Docked(Actions(cancel, ok), Dock.Bottom), new StackPanel { Spacing = 8, Children = { Text(title, "h1"), Text(message, "sub") } } }
        };
        return await window.ShowDialog<bool>(owner);
    }

    internal static T Docked<T>(T control, Dock dock) where T : Control
    {
        DockPanel.SetDock(control, dock);
        return control;
    }

    /// <summary>One source per line: a plain URL, or a JSON object for POST requests, joins and detail templates.</summary>
    internal static string Format(IEnumerable<SourceSpec> sources) => string.Join(Environment.NewLine, sources.Select(s =>
        s with { Url = "" } == new SourceSpec { Name = s.Name } && s.Name is null ? s.Url : JsonSerializer.Serialize(s, Compact)));

    internal static List<SourceSpec> ParseSources(string text, out string? error)
    {
        error = null;
        var list = new List<SourceSpec>();
        foreach (var raw in text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')))
        {
            try
            {
                var source = raw.StartsWith('{') ? JsonSerializer.Deserialize<SourceSpec>(raw, Compact) : new SourceSpec { Url = raw };
                if (source is null || !Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                {
                    error = "不是有效的 http(s) 链接：" + (raw.Length > 80 ? raw[..80] + "…" : raw);
                    return [];
                }
                list.Add(source);
            }
            catch (JsonException ex)
            {
                error = "JSON 格式错误：" + ex.Message;
                return [];
            }
        }
        return list;
    }

    internal static readonly JsonSerializerOptions Compact = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}

/// <summary>Choose built-in games and servers, or describe a custom game by its announcement sources.</summary>
internal sealed class AddGameDialog : Window
{
    public AddGameDialog(IReadOnlySet<string> existing)
    {
        Title = "添加游戏 / 区服";
        Width = 720; Height = 560; MinWidth = 600; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var checks = new List<(CheckBox Box, string Template)>();
        var regions = new StackPanel { Spacing = 6, Margin = new Thickness(16, 4, 4, 4) };
        var games = new ListBox { ItemsSource = GameCatalog.Games.Select(g => g.Name).ToList(), SelectedIndex = 0, Width = 220 };
        games.Classes.Add("plain");
        void ShowRegions()
        {
            regions.Children.Clear();
            if (games.SelectedIndex < 0) return;
            var game = GameCatalog.Games[games.SelectedIndex];
            regions.Children.Add(Dialogs.Text(game.Name, "h1"));
            regions.Children.Add(Dialogs.Text(game.Publisher, "sub"));
            regions.Children.Add(Dialogs.Text("选择要关注的服务器（不同区服的活动时间不同，分别添加）：", "sub"));
            foreach (var region in game.Regions)
            {
                var template = $"{game.Id}/{region.Id}";
                var previous = checks.FirstOrDefault(c => c.Template == template).Box;
                var box = previous ?? new CheckBox
                {
                    Content = region.Name + (existing.Contains(template) ? "（已添加）" : ""),
                    IsEnabled = !existing.Contains(template)
                };
                ToolTip.SetTip(box, string.Join("\n", region.Sources.Select(s => $"{s.Name}：{s.Url}")));
                if (previous is null) checks.Add((box, template));
                regions.Children.Add(box);
            }
        }
        games.SelectionChanged += (_, _) => ShowRegions();
        ShowRegions();

        var name = new TextBox { Watermark = "游戏名称，例如：蔚蓝档案" };
        var region = new TextBox { Watermark = "区服，例如：国服 / 日服 / 国际服" };
        var sources = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, Height = 110, Watermark = "公告来源，每行一个链接（官网新闻页、RSS、JSON 接口或 ICS 日历）" };
        var error = Dialogs.Text("", null);
        error.Foreground = Brushes.IndianRed;
        var custom = new StackPanel
        {
            Spacing = 8, Margin = new Thickness(4, 8),
            Children =
            {
                Dialogs.Text("不在列表中的游戏：填写名称和公告来源。程序会自动识别 JSON / HTML / RSS / Atom / ICS，并跟进页面中的公告链接。", "sub"),
                name, region, sources,
                Dialogs.Text("进阶：一行也可以是 JSON，例如 {\"url\":\"…\",\"method\":\"POST\",\"body\":\"…\",\"detail\":{\"url\":\"…/{id}\"}}，{字段} 会用列表中的同名字段替换。", "sub"),
                error
            }
        };
        var tabs = new TabControl
        {
            ItemsSource = new[]
            {
                new TabItem { Header = "内置游戏", Content = new DockPanel { Children = { Dialogs.Docked(games, Dock.Left), new ScrollViewer { Content = regions } } } },
                new TabItem { Header = "自定义游戏", Content = new ScrollViewer { Content = custom } }
            }
        };

        var add = Dialogs.Button("添加", primary: true);
        var cancel = Dialogs.Button("取消");
        cancel.Click += (_, _) => Close(null);
        add.Click += (_, _) =>
        {
            if (tabs.SelectedIndex == 1)
            {
                var list = Dialogs.ParseSources(sources.Text ?? "", out var problem);
                if (string.IsNullOrWhiteSpace(name.Text) || list.Count == 0 || problem is not null)
                {
                    error.Text = problem ?? "请填写游戏名称和至少一个公告来源。";
                    return;
                }
                var id = "custom-" + Guid.NewGuid().ToString("N")[..8];
                Close(new List<GameConfig> { new() { Id = id, Name = name.Text.Trim(), Region = region.Text?.Trim() ?? "", Publisher = "自定义", Color = "#6B7A90", Sources = list } });
                return;
            }
            Close(checks.Where(c => c.Box.IsChecked == true).Select(c => GameCatalog.Create(c.Template)).OfType<GameConfig>().ToList());
        };
        Content = new DockPanel { Margin = new Thickness(16), Children = { Dialogs.Docked(Dialogs.Actions(cancel, add), Dock.Bottom), tabs } };
    }
}

/// <summary>Edit one game's announcement sources.</summary>
internal sealed class SourcesDialog : Window
{
    public SourcesDialog(GameConfig game)
    {
        Title = "公告来源 · " + game.DisplayName;
        Width = 760; Height = 460; MinWidth = 560; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var editor = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas, Menlo, monospace"), FontSize = 12, Text = Dialogs.Format(game.Sources) };
        var error = Dialogs.Text("");
        error.Foreground = Brushes.IndianRed;
        var save = Dialogs.Button("保存", primary: true);
        var cancel = Dialogs.Button("取消");
        var reset = Dialogs.Button("恢复默认来源");
        reset.IsVisible = game.Template is not null;
        reset.Click += (_, _) => editor.Text = Dialogs.Format(GameCatalog.Create(game.Template!)?.Sources ?? []);
        cancel.Click += (_, _) => Close(null);
        save.Click += (_, _) =>
        {
            var list = Dialogs.ParseSources(editor.Text ?? "", out var problem);
            if (problem is not null || list.Count == 0) { error.Text = problem ?? "至少需要一个来源。"; return; }
            var standard = game.Template is { } t && GameCatalog.Create(t) is { } fresh && Dialogs.Format(fresh.Sources) == Dialogs.Format(list);
            Close(game with { Sources = list, CustomSources = !standard });
        };
        var actions = Dialogs.Actions(cancel, save);
        actions.Children.Insert(0, reset);
        Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                Dialogs.Docked(Dialogs.Text("每行一个来源：网页 / RSS / JSON / ICS 链接，或描述请求方式、正文拼接、详情模板的 JSON。同一通知出现在多个来源时会自动合并。", "sub"), Dock.Top),
                Dialogs.Docked(new StackPanel { Children = { error, actions } }, Dock.Bottom),
                new Border { Margin = new Thickness(0, 10), Child = editor }
            }
        };
    }
}

internal sealed class SettingsDialog : Window
{
    private static readonly (int Hours, string Label)[] Intervals = [(0, "不自动刷新"), (1, "每 1 小时"), (3, "每 3 小时"), (6, "每 6 小时"), (12, "每 12 小时"), (24, "每天")];
    private static readonly (string Key, string Label)[] Themes = [("system", "跟随系统"), ("light", "浅色"), ("dark", "深色")];

    public SettingsDialog(MainViewModel model)
    {
        Title = "设置";
        Width = 460; Height = 380; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var settings = model.Settings;
        var retention = new NumericUpDown { Minimum = 1, Maximum = 12, Value = settings.RetentionMonths, FormatString = "0", Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
        var refresh = new ComboBox { ItemsSource = Intervals.Select(i => i.Label).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(Intervals, i => i.Hours == settings.RefreshHours)), Width = 200 };
        var theme = new ComboBox { ItemsSource = Themes.Select(t => t.Label).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(Themes, t => t.Key == settings.Theme)), Width = 200 };
        var folder = Dialogs.Button("打开数据文件夹");
        folder.HorizontalAlignment = HorizontalAlignment.Left;
        folder.Click += async (_, _) => await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(model.DataFolder));
        var save = Dialogs.Button("保存", primary: true);
        var cancel = Dialogs.Button("取消");
        cancel.Click += (_, _) => Close();
        save.Click += (_, _) =>
        {
            model.ApplySettings((int)(retention.Value ?? 3), Intervals[Math.Max(0, refresh.SelectedIndex)].Hours, Themes[Math.Max(0, theme.SelectedIndex)].Key);
            Close();
        };
        Content = new DockPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                Dialogs.Docked(Dialogs.Actions(cancel, save), Dock.Bottom),
                new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        Dialogs.Text("保留已结束的活动（月）", "section"), retention,
                        Dialogs.Text("自动刷新", "section"), refresh,
                        Dialogs.Text("外观", "section"), theme,
                        Dialogs.Text("数据", "section"), Dialogs.Text(model.DataFolder, "sub"), folder
                    }
                }
            }
        };
    }
}

/// <summary>What each source returned on the last refresh.</summary>
internal sealed class ReportsDialog : Window
{
    public ReportsDialog(MainViewModel model)
    {
        Title = "来源状态";
        Width = 820; Height = 560; MinWidth = 600; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var names = model.Games.ToDictionary(g => g.Id, g => g.Config.DisplayName);
        var list = new StackPanel { Spacing = 4 };
        foreach (var report in model.Reports.OrderBy(r => r.State == SourceState.Ok).ThenBy(r => names.GetValueOrDefault(r.GameId)))
        {
            var (mark, color) = report.State switch
            {
                SourceState.Ok => ("●", "#22A06B"),
                SourceState.Partial => ("◐", "#E2A400"),
                SourceState.Empty => ("○", "#8C96AA"),
                SourceState.Cached => ("◑", "#E2A400"),
                _ => ("✕", "#E5484D")
            };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            head.Children.Add(new TextBlock { Text = mark, Foreground = new SolidColorBrush(Color.Parse(color)) });
            head.Children.Add(new TextBlock { Text = names.GetValueOrDefault(report.GameId, report.GameId), FontWeight = FontWeight.SemiBold });
            head.Children.Add(Dialogs.Text($"{report.Source} · {report.Count} 条", "sub", wrap: false));
            var url = Dialogs.Text(report.Url, "sub");
            url.FontSize = 11;
            var card = new Border { Padding = new Thickness(12, 8), Child = new StackPanel { Spacing = 2, Children = { head, Dialogs.Text(report.Message), url } } };
            card.Classes.Add("card");
            list.Children.Add(card);
        }
        if (model.Reports.Count == 0) list.Children.Add(Dialogs.Text("还没有刷新记录。", "sub"));
        var close = Dialogs.Button("关闭", primary: true);
        close.Click += (_, _) => Close();
        Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                Dialogs.Docked(Dialogs.Text("● 正常  ◐ 部分详情失败  ◑ 失败但使用了缓存  ○ 没有识别到条目  ✕ 失败", "sub"), Dock.Top),
                Dialogs.Docked(Dialogs.Actions(close), Dock.Bottom),
                new ScrollViewer { Margin = new Thickness(0, 10, 0, 0), Content = list }
            }
        };
    }
}
