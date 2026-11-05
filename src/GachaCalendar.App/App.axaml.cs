using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using GachaCalendar.App.ViewModels;
using GachaCalendar.App.Views;
using GachaCalendar.Core;

namespace GachaCalendar.App;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var model = new MainViewModel(new AppStore());
            ApplyTheme(model.Theme);
            desktop.MainWindow = new MainWindow { DataContext = model };
            desktop.ShutdownRequested += (_, _) => model.Save();
        }
        base.OnFrameworkInitializationCompleted();
    }

    public static void ApplyTheme(string theme)
    {
        if (Current is { } app)
            app.RequestedThemeVariant = theme switch { "light" => ThemeVariant.Light, "dark" => ThemeVariant.Dark, _ => ThemeVariant.Default };
    }
}
