using System.ComponentModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Nucs.JsonSettings.Examples.UI.Avalonia;

public partial class App : Application {
    public override void Initialize() {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted() {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            var settings = DemoSettings.Instance;   // first file touch, on the UI thread

            // The settings object IS the window's DataContext — no view model wrapper. The
            // compiled bindings in MainWindow.axaml subscribe to the INotifyPropertyChanged
            // that [NotifyChangesMixin] injected at build time.
            ApplyTheme(settings.DarkMode);

            // DemoSettings does not implement INotifyPropertyChanged in SOURCE — the interface
            // is woven in — so code (unlike XAML bindings, which probe the instance) has to
            // cast through object to reach it.
            ((INotifyPropertyChanged)(object)settings).PropertyChanged += (_, e) => {
                if (e.PropertyName == nameof(DemoSettings.DarkMode))
                    ApplyTheme(settings.DarkMode);
            };

            desktop.MainWindow = new MainWindow { DataContext = settings };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ApplyTheme(bool dark) =>
        RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
}
