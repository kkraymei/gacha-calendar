using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GachaCalendar.App.ViewModels;

namespace GachaCalendar.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private MainViewModel Model => (MainViewModel)DataContext!;

    private static GameItem? GameOf(object? sender) => (sender as Control)?.DataContext as GameItem;

    private async void OnAddGame(object? sender, RoutedEventArgs e)
    {
        var picked = await new AddGameDialog(Model.Games.Select(g => g.Config.Template).OfType<string>().ToHashSet()).ShowDialog<List<Core.GameConfig>?>(this);
        if (picked is { Count: > 0 }) Model.AddGames(picked);
    }

    private async void OnEditSources(object? sender, RoutedEventArgs e)
    {
        if (GameOf(sender) is not { } game) return;
        var updated = await new SourcesDialog(game.Config).ShowDialog<Core.GameConfig?>(this);
        if (updated is not null) Model.UpdateGame(game, updated);
    }

    private void OnMoveUp(object? sender, RoutedEventArgs e) { if (GameOf(sender) is { } game) Model.MoveGame(game, -1); }

    private void OnMoveDown(object? sender, RoutedEventArgs e) { if (GameOf(sender) is { } game) Model.MoveGame(game, 1); }

    private async void OnRemoveGame(object? sender, RoutedEventArgs e)
    {
        if (GameOf(sender) is not { } game) return;
        if (await Dialogs.Confirm(this, $"移除「{game.Config.DisplayName}」？", "已获取的条目和该游戏的设置会一并移除，之后可以重新添加。"))
            Model.RemoveGame(game);
    }

    private async void OnSettings(object? sender, RoutedEventArgs e) => await new SettingsDialog(Model).ShowDialog(this);

    private async void OnReports(object? sender, RoutedEventArgs e) => await new ReportsDialog(Model).ShowDialog(this);

    private void OnRestoreHidden(object? sender, RoutedEventArgs e) => Model.RestoreHiddenCommand.Execute(null);

    private void OnToday(object? sender, RoutedEventArgs e) => Timeline.GoToday();

    private void OnCloseDetail(object? sender, RoutedEventArgs e) => Model.Selected = null;

    private async void OnOpenEntry(object? sender, RoutedEventArgs e)
    {
        if (Model.Selected?.Entry.Url is { Length: > 0 } url) await Open(url);
    }

    private async void OnOpenOrigin(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is string { Length: > 0 } url) await Open(url);
    }

    private async Task Open(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) await Launcher.LaunchUriAsync(uri);
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出日历",
            SuggestedFileName = $"游戏活动日历-{DateTime.Today:yyyyMMdd}.ics",
            DefaultExtension = "ics",
            FileTypeChoices = [new FilePickerFileType("iCalendar") { Patterns = ["*.ics"], MimeTypes = ["text/calendar"] }]
        });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
        await writer.WriteAsync(Model.ExportIcs());
    }
}
