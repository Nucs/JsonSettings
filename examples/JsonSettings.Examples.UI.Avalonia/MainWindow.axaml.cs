using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.NotifyChanges;

namespace Nucs.JsonSettings.Examples.UI.Avalonia;

public partial class MainWindow : Window {
    private readonly DemoSettings _settings = DemoSettings.Instance;
    private int _saveCount;

    public MainWindow() {
        InitializeComponent();

        // Restore the persisted bounds before the window is shown.
        Width = _settings.WindowWidth;
        Height = _settings.WindowHeight;

        PathLabel.Text = $"file: {_settings.FileName}";

        // Save feedback. AfterSave is a plain settings event, not a UI notification, so it is
        // NOT marshalled by EnableNotificationMarshaling() and can fire on whatever thread
        // performed the write (the background counter button, for one) — post to the UI thread
        // ourselves.
        _settings.AfterSave += (_, destination) => Dispatcher.UIThread.Post(() =>
            SaveStatus.Text = $"save #{++_saveCount} → {destination}");

        Opened += (_, _) => {
            // Captures the UI thread's SynchronizationContext, so a PropertyChanged raised by a
            // background-thread write is posted back here and the bound controls update legally.
            // Called from Opened because that is the earliest point this window is certain to be
            // running inside the dispatcher loop that owns the context.
            _settings.EnableNotificationMarshaling();
        };
    }

    private void OnBackgroundIncrement(object? sender, RoutedEventArgs e) {
        // A woven setter invoked OFF the UI thread: the save runs right here on the worker,
        // while the PropertyChanged for the bound TextBlock is marshalled to the UI thread.
        _ = Task.Run(() => _settings.Counter++);
    }

    private void OnAddTag(object? sender, RoutedEventArgs e) {
        var tag = NewTagBox.Text?.Trim();
        if (string.IsNullOrEmpty(tag))
            return;
        // No setter runs here — the collection instance is unchanged. EnableAutosave() bound
        // the ObservableCollection's INotifyCollectionChanged, so the Add itself persists.
        _settings.Tags.Add(tag);
        NewTagBox.Text = "";
    }

    private void OnRemoveTag(object? sender, RoutedEventArgs e) {
        if (TagsList.SelectedItem is string tag)
            _settings.Tags.Remove(tag);
    }

    protected override void OnClosing(WindowClosingEventArgs e) {
        // Two writes, one file save: SuspendAutosave() batches the woven setters until the
        // scope closes. Without it, closing the window would write the file twice back to back.
        using (_settings.SuspendAutosave()) {
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
        }
        base.OnClosing(e);
    }
}
